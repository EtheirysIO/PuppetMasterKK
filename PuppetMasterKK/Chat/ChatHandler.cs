using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using ECommons.Automation;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Diagnostics;
using Dalamud.Game.Chat;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;

namespace PuppetMasterKK
{
    public partial class ChatHandler
    {
        private static readonly ReactionExecutionGate ExecutionGate = new();
        private static readonly ConcurrentDictionary<long, Task> ActiveTasks = new();
        private static readonly ConcurrentDictionary<Reaction, CancellationTokenSource> ActiveReactionCancellations =
            new(ReferenceEqualityComparer.Instance);
        private static readonly ConcurrentDictionary<IActiveNotification, byte> ActiveNotifications = new();
        private static ConditionalWeakTable<Reaction, SuppressionNotificationState> suppressionNotifications = new();
        private static ConditionalWeakTable<Reaction, ErrorNotificationState> errorNotifications = new();
        private static ConditionalWeakTable<Reaction, ReactionControlState> reactionControls = new();
        private static ConditionalWeakTable<Reaction, BoundedRetriggerScheduler<PendingRetrigger>> retriggerQueues = new();
        private static CancellationTokenSource pluginLifetime = new();
        private static long nextTaskId;
        private static long nextVisualizerReactionId;
        private static long droppedMessageCount;
        private static long droppedRetriggerCount;
        private static int shuttingDown;
        private static Channel<ChatEnvelope> dispatcher = CreateDispatcher();

        private sealed record ReactionSnapshot(
            Reaction Source,
            ReactionControlState Control,
            long Generation,
            string Name,
            bool MotionOnly,
            bool AllowAllCommands,
            ReactionExecutionPolicy ExecutionPolicy,
            bool ShowNotifications,
            bool ShowSuppressionNotifications,
            int CooldownSeconds,
            HashSet<int> EnabledChannels,
            HashSet<string> CommandWhitelist,
            HashSet<string> CommandBlacklist,
            SenderFilter Senders,
            Regex Pattern,
            string Replacement,
            bool TemplateHasWait,
            ProtectionSettings Protections,
            bool NoProtections);

        private sealed record ChatEnvelope(XivChatType Type, string Message, List<ReactionSnapshot> Reactions);
        private sealed record PendingRetrigger(
            ReactionSnapshot Reaction,
            string Command,
            CancellationToken PluginToken);

        private sealed class SuppressionNotificationState
        {
            public int PendingCount;
            public long NextNotificationTimestamp;
        }

        private sealed class ReactionControlState
        {
            public long Generation;
            public long VisualizerId = Interlocked.Increment(ref nextVisualizerReactionId);
        }

        private sealed class ErrorNotificationState
        {
            public long NextNotificationTimestamp;
        }

        public ChatHandler()
        {
        }

        public static void Initialize()
        {
            Volatile.Write(ref shuttingDown, 0);
            var previous = Interlocked.Exchange(ref pluginLifetime, new CancellationTokenSource());
            previous.Cancel();
            dispatcher.Writer.TryComplete();
            dispatcher = CreateDispatcher();
            Interlocked.Exchange(ref droppedMessageCount, 0);
            Interlocked.Exchange(ref droppedRetriggerCount, 0);
            ExecutionGate.Reset();
            ReactionVisualizerState.Reset();
            suppressionNotifications = new ConditionalWeakTable<Reaction, SuppressionNotificationState>();
            errorNotifications = new ConditionalWeakTable<Reaction, ErrorNotificationState>();
            reactionControls = new ConditionalWeakTable<Reaction, ReactionControlState>();
            retriggerQueues = new ConditionalWeakTable<Reaction, BoundedRetriggerScheduler<PendingRetrigger>>();
            var currentDispatcher = dispatcher;
            var token = pluginLifetime.Token;
            // One reader: matching is cheap and runs are started without awaiting them, and a single reader keeps
            // triggers in chat order (several readers made "queue latest" keep an older trigger).
            Track(Task.Run(() => DispatchLoopAsync(currentDispatcher.Reader, token), token));
        }

        public static void Shutdown()
        {
            FollowMode.Reset();
            Volatile.Write(ref shuttingDown, 1);
            pluginLifetime.Cancel();
            dispatcher.Writer.TryComplete();
            foreach (var cancellation in ActiveReactionCancellations.Values)
            {
                try { cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }
            foreach (var notification in ActiveNotifications.Keys)
            {
                if (notification.DismissReason == null)
                    notification.DismissNow();
            }
            ActiveNotifications.Clear();
            ExecutionGate.Reset();
            ReactionVisualizerState.Reset();
            suppressionNotifications = new ConditionalWeakTable<Reaction, SuppressionNotificationState>();
            errorNotifications = new ConditionalWeakTable<Reaction, ErrorNotificationState>();
            reactionControls = new ConditionalWeakTable<Reaction, ReactionControlState>();
            retriggerQueues = new ConditionalWeakTable<Reaction, BoundedRetriggerScheduler<PendingRetrigger>>();
        }

        public static void CancelReaction(Reaction reaction)
        {
            InvalidateReaction(reaction, true);
        }

        public static void InvalidateReaction(Reaction reaction, bool cancelActive)
        {
            var control = reactionControls.GetValue(reaction, static _ => new ReactionControlState());
            Interlocked.Increment(ref control.Generation);
            if (cancelActive && ActiveReactionCancellations.TryGetValue(reaction, out var cancellation))
            {
                try { cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }
            CancelQueuedRetriggers(reaction);
        }

        private static void CancelQueuedRetriggers(Reaction reaction)
        {
            if (reactionControls.TryGetValue(reaction, out var control))
                ReactionVisualizerState.ClearQueued(control.VisualizerId);
            if (!retriggerQueues.TryGetValue(reaction, out var state))
                return;
            state.Cancel();
        }

        public static long DroppedMessageCount => Interlocked.Read(ref droppedMessageCount);
        public static long DroppedRetriggerCount => Interlocked.Read(ref droppedRetriggerCount);

        internal static long GetVisualizerId(Reaction reaction)
        {
            return reactionControls.GetValue(reaction, static _ => new ReactionControlState()).VisualizerId;
        }

        public static void ResetDroppedMessageCount()
        {
            Interlocked.Exchange(ref droppedMessageCount, 0);
            Interlocked.Exchange(ref droppedRetriggerCount, 0);
        }

        private static Channel<ChatEnvelope> CreateDispatcher()
        {
            return Channel.CreateBounded<ChatEnvelope>(new BoundedChannelOptions(128)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            }, _ => Interlocked.Increment(ref droppedMessageCount));
        }

        private static async Task DispatchLoopAsync(ChannelReader<ChatEnvelope> reader, CancellationToken token)
        {
            try
            {
                await foreach (var envelope in reader.ReadAllAsync(token))
                {
                    foreach (var reaction in envelope.Reactions)
                    {
                        var task = DoCommandAsync(reaction, envelope.Type, envelope.Message, token);
                        if (!task.IsCompletedSuccessfully)
                            Track(task);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        private static ReactionSnapshot? CreateSnapshot(
            Reaction reaction,
            bool showNotifications,
            bool showSuppressionNotifications)
        {
            if (!reaction.Enabled)
                return null;
            var pattern = reaction.UseRegex ? reaction.CustomRx : reaction.Rx;
            if (pattern == null)
                return null;
            var control = reactionControls.GetValue(reaction, static _ => new ReactionControlState());

            return new ReactionSnapshot(
                reaction,
                control,
                Volatile.Read(ref control.Generation),
                reaction.Name,
                reaction.MotionOnly,
                reaction.AllowAllCommands,
                reaction.ExecutionPolicy,
                PluginUiLogic.ResolveNotificationSetting(reaction.ProgressNotifications, showNotifications),
                PluginUiLogic.ResolveNotificationSetting(reaction.SuppressedNotifications, showSuppressionNotifications),
                Math.Max(0, reaction.CooldownSeconds),
                new HashSet<int>(reaction.EnabledChannels),
                Service.Commands.CanonicalSet(reaction.CommandWhitelist),
                Service.Commands.CanonicalSet(reaction.CommandBlacklist),
                (reaction.Senders ?? SenderFilter.AnyoneFilter()).Clone(),
                pattern,
                reaction.UseRegex ? reaction.ReplaceMatch : Service.GetDefaultReplaceMatch(),
                ReactionCommandMatcher.TemplateHasWait(reaction.UseRegex ? reaction.ReplaceMatch : Service.GetDefaultReplaceMatch()),
                (reaction.Protections ?? new ProtectionSettings()).Clone(),
                reaction.NoProtections);
        }

        private static void Track(Task task)
        {
            var id = Interlocked.Increment(ref nextTaskId);
            ActiveTasks[id] = task;
            _ = task.ContinueWith(
                completed =>
                {
                    ActiveTasks.TryRemove(id, out _);
                    if (completed.Exception != null)
                        Service.PluginLog.Error(completed.Exception, "PuppetMasterKK background task failed.");
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static async Task RunMacroAsync(
            string[] lines,
            ReactionSnapshot reaction,
            CancellationTokenSource cancellation,
            CancellationToken pluginToken)
        {
            IActiveNotification? notification = null;

            if (reaction.ShowNotifications && !cancellation.IsCancellationRequested)
            {
                await Service.Framework.RunOnFrameworkThread(() =>
                {
                    if (cancellation.IsCancellationRequested)
                        return;
                    notification = Service.NotificationManager.AddNotification(new Notification
                    {
                        Title = "PuppetMasterKK",
                        Content = $"Starting trigger: {reaction.Name}",
                        Type = NotificationType.Info,
                        Progress = 0,
                        InitialDuration = TimeSpan.MaxValue,
                        UserDismissable = false,
                    });
                    ActiveNotifications[notification] = 0;

                    notification.DrawActions += _ =>
                    {
                        if (ImGui.Button($"Cancel##PuppetMasterKKReaction{notification.Id}"))
                        {
                            try { cancellation.Cancel(); }
                            catch (ObjectDisposedException) { }
                        }
                    };
                });
            }

            try
            {
                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var textCommand = Service.FormatCommand(lines[lineIndex]);
                    if (string.IsNullOrEmpty(textCommand.Main))
                        continue;

                    if (notification != null)
                    {
                        await UpdateReactionNotificationAsync(
                            notification,
                            $"{reaction.Name}\nStep {lineIndex + 1} of {lines.Length}: {textCommand}",
                            (float)lineIndex / lines.Length,
                            cancellation.Token);
                    }

                    var catalog = Service.Commands;
                    var canonical = catalog.Canonicalize(textCommand.Main);

                    // Emotes never carry the sender's text unless the reaction allows it.
                    if (reaction.MotionOnly && catalog.IsEmote(canonical))
                        textCommand.Args = "motion";

                    if (CommandCatalog.Normalize(textCommand.Main) == CommandPolicy.WaitCommand)
                    {
                        // Plugin-internal pause, not a game command (see CommandPolicy.IsWaitAllowed).
                        if (CommandPolicy.IsWaitAllowed(catalog, reaction.TemplateHasWait, reaction.CommandWhitelist,
                                                        reaction.CommandBlacklist, out _, reaction.NoProtections) &&
                            ReactionCommandMatcher.TryParseWaitSeconds(textCommand.Args, out var seconds))
                            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellation.Token);
                    }
                    else
                    {
                        try
                        {
                            // The permission check runs on the framework thread (it reads the registered plugin
                            // commands), before a send slot is taken: blocked lines must not use up the rate limit.
                            var allowed = await Service.Framework.RunOnFrameworkThread(() =>
                            {
                                var kind = catalog.Classify(textCommand.Main, Service.IsPluginCommand);
                                if (CommandPolicy.IsAllowed(
                                        canonical,
                                        kind,
                                        reaction.CommandWhitelist,
                                        reaction.CommandBlacklist,
                                        reaction.AllowAllCommands,
                                        out var permissionReason,
                                        reaction.NoProtections,
                                        Service.IsOpen(reaction.Protections, kind, canonical, textCommand.Main)))
                                    return true;
                                Service.PluginLog.Debug("{Reaction}: {Command} blocked: {Reason}", reaction.Name, textCommand.Main, permissionReason);
                                return false;
                            });

                            if (allowed)
                            {
                                // One shared limit on everything PuppetMasterKK sends. A slot is taken only when it's
                                // free, so a run cancelled while waiting leaves nothing booked.
                                while (!CommandRateLimiter.Shared.TryAcquire(Stopwatch.GetTimestamp()))
                                {
                                    var wait = CommandRateLimiter.Shared.TimeUntilFree(Stopwatch.GetTimestamp());
                                    await Task.Delay(wait > TimeSpan.FromMilliseconds(10) ? wait : TimeSpan.FromMilliseconds(10), cancellation.Token);
                                }

                                await Service.Framework.RunOnFrameworkThread(() =>
                                {
                                    if (cancellation.IsCancellationRequested)
                                        return;
                                    try
                                {
                                        Chat.SendMessage(textCommand.ToString());
                                    }
                                    catch (Exception ex)
                                    {
                                        Service.ChatGui.PrintError($"[PuppetMasterKK] Failed to send command {textCommand}: {ex.Message}");
                                    }
                                });
                            }
                            cancellation.Token.ThrowIfCancellationRequested();
                        }
                        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            PrintErrorOnFramework($"[PuppetMasterKK] Framework thread execution failed: {ex.Message}", pluginToken);
                        }
                    }
                    if (notification != null)
                    {
                        await UpdateReactionNotificationAsync(
                            notification,
                            $"{reaction.Name}\nCompleted step {lineIndex + 1} of {lines.Length}",
                            (float)(lineIndex + 1) / lines.Length,
                            cancellation.Token);
                    }
                }

                if (notification != null && !pluginToken.IsCancellationRequested)
                    await FinishReactionNotificationAsync(notification, reaction.Name, false, pluginToken: pluginToken);
            }
            catch (OperationCanceledException)
            {
                if (notification != null && !pluginToken.IsCancellationRequested)
                    await FinishReactionNotificationAsync(notification, reaction.Name, true, pluginToken: pluginToken);
            }
            catch (Exception ex)
            {
                if (notification != null && !pluginToken.IsCancellationRequested)
                    await FinishReactionNotificationAsync(notification, reaction.Name, false, ex.Message, pluginToken);
                else
                    PrintErrorOnFramework($"[PuppetMasterKK] Trigger {reaction.Name} failed: {ex.Message}", pluginToken);
            }
            finally
            {
                // A notification must never outlive its run (its Cancel button closes over a disposed token source).
                if (notification != null && notification.DismissReason == null && !pluginToken.IsCancellationRequested)
                {
                    var leftover = notification;
                    try
                    {
                        await Service.Framework.RunOnFrameworkThread(() =>
                        {
                            if (leftover.DismissReason == null)
                                leftover.DismissNow();
                            ActiveNotifications.TryRemove(leftover, out _);
                        });
                    }
                    catch (Exception ex)
                    {
                        Service.PluginLog.Warning(ex, "Could not dismiss a PuppetMasterKK progress notification.");
                    }
                }
            }
        }

        // IChatGui must be used from the framework thread; background runs route their messages through here.
        private static void PrintErrorOnFramework(string message, CancellationToken pluginToken)
        {
            if (pluginToken.IsCancellationRequested || Volatile.Read(ref shuttingDown) != 0)
                return;
            Track(Service.Framework.RunOnFrameworkThread(() =>
            {
                if (!pluginToken.IsCancellationRequested && Volatile.Read(ref shuttingDown) == 0)
                    Service.ChatGui.PrintError(message);
            }));
        }

        private static async Task NotifySuppressionAsync(
            ReactionSnapshot reaction,
            ReactionRejectionReason reason,
            CancellationToken pluginToken)
        {
            if (!reaction.ShowSuppressionNotifications || pluginToken.IsCancellationRequested)
                return;

            var state = suppressionNotifications.GetValue(reaction.Source, static _ => new SuppressionNotificationState());
            var nowTimestamp = Stopwatch.GetTimestamp();
            int suppressedCount;
            lock (state)
            {
                state.PendingCount++;
                if (nowTimestamp < state.NextNotificationTimestamp)
                    return;
                suppressedCount = state.PendingCount;
                state.PendingCount = 0;
                state.NextNotificationTimestamp = nowTimestamp + 5 * Stopwatch.Frequency;
            }

            var reasonText = reason == ReactionRejectionReason.Busy
                ? "busy running or processing queued triggers"
                : "cooldown active";
            var countText = suppressedCount > 1 ? $"\n{suppressedCount} repeated triggers suppressed." : string.Empty;
            try
            {
                await Service.Framework.RunOnFrameworkThread(() =>
                {
                    if (pluginToken.IsCancellationRequested)
                        return;
                    Service.NotificationManager.AddNotification(new Notification
                    {
                        Title = "PuppetMasterKK",
                        Content = $"Trigger suppressed: {reaction.Name}\n{reasonText}.{countText}",
                        Type = NotificationType.Warning,
                        InitialDuration = TimeSpan.FromSeconds(4),
                    });
                });
            }
            catch (Exception) when (pluginToken.IsCancellationRequested)
            {
            }
        }

        private static async Task NotifyReactionErrorAsync(
            ReactionSnapshot reaction,
            string error,
            CancellationToken pluginToken)
        {
            if (!reaction.ShowNotifications || pluginToken.IsCancellationRequested)
                return;
            var state = errorNotifications.GetValue(reaction.Source, static _ => new ErrorNotificationState());
            var nowTimestamp = Stopwatch.GetTimestamp();
            lock (state)
            {
                if (nowTimestamp < state.NextNotificationTimestamp)
                    return;
                state.NextNotificationTimestamp = nowTimestamp + 5 * Stopwatch.Frequency;
            }

            try
            {
                await Service.Framework.RunOnFrameworkThread(() =>
                {
                    if (pluginToken.IsCancellationRequested)
                        return;
                    Service.NotificationManager.AddNotification(new Notification
                    {
                        Title = "PuppetMasterKK",
                        Content = $"Trigger failed: {reaction.Name}\n{error}",
                        Type = NotificationType.Error,
                        InitialDuration = TimeSpan.FromSeconds(5),
                    });
                });
            }
            catch (Exception) when (pluginToken.IsCancellationRequested)
            {
            }
        }

        private static async Task NotifySchedulerFailureAsync(
            Reaction reaction,
            string error,
            int discarded,
            CancellationToken pluginToken)
        {
            if (Volatile.Read(ref shuttingDown) != 0 ||
                pluginToken.IsCancellationRequested ||
                Service.configuration?.ShowReactionNotifications != true)
                return;

            var state = errorNotifications.GetValue(reaction, static _ => new ErrorNotificationState());
            var nowTimestamp = Stopwatch.GetTimestamp();
            lock (state)
            {
                if (nowTimestamp < state.NextNotificationTimestamp)
                    return;
                state.NextNotificationTimestamp = nowTimestamp + 5 * Stopwatch.Frequency;
            }

            try
            {
                await Service.Framework.RunOnFrameworkThread(() =>
                {
                    if (Volatile.Read(ref shuttingDown) != 0 || pluginToken.IsCancellationRequested)
                        return;
                    Service.NotificationManager.AddNotification(new Notification
                    {
                        Title = "PuppetMasterKK",
                        Content = $"Trigger scheduler failed: {reaction.Name}\n{error}" +
                                  (discarded > 0 ? $"\n{discarded} pending trigger(s) discarded." : string.Empty),
                        Type = NotificationType.Error,
                        InitialDuration = TimeSpan.FromSeconds(6),
                    });
                });
            }
            catch (Exception) when (pluginToken.IsCancellationRequested || Volatile.Read(ref shuttingDown) != 0)
            {
            }
        }

        private static Task UpdateReactionNotificationAsync(
            IActiveNotification notification,
            string content,
            float progress,
            CancellationToken token)
        {
            return Service.Framework.RunOnFrameworkThread(() =>
            {
                if (!token.IsCancellationRequested && notification.DismissReason == null)
                {
                    notification.Content = content;
                    notification.Progress = Math.Clamp(progress, 0, 1);
                }
            });
        }

        private static Task FinishReactionNotificationAsync(
            IActiveNotification notification,
            string reactionName,
            bool cancelled,
            string? error = null,
            CancellationToken pluginToken = default)
        {
            return Service.Framework.RunOnFrameworkThread(() =>
            {
                if (pluginToken.IsCancellationRequested)
                    return;
                notification.DismissNow();
                ActiveNotifications.TryRemove(notification, out _);
                Service.NotificationManager.AddNotification(new Notification
                {
                    Title = "PuppetMasterKK",
                    Content = error != null
                        ? $"Trigger failed: {reactionName}\n{error}"
                        : cancelled
                            ? $"Cancelled trigger: {reactionName}"
                            : $"Completed trigger: {reactionName}",
                    Type = error != null
                        ? NotificationType.Error
                        : cancelled ? NotificationType.Warning : NotificationType.Success,
                    InitialDuration = TimeSpan.FromSeconds(4),
                });
            });
        }

        private static async Task DoCommandAsync(ReactionSnapshot reaction, XivChatType type, string message, CancellationToken pluginToken)
        {
            if (!reaction.EnabledChannels.Contains((int)type) || pluginToken.IsCancellationRequested ||
                Volatile.Read(ref reaction.Control.Generation) != reaction.Generation)
                return;

            var matchStatus = ReactionCommandMatcher.TryGenerateCommand(
                reaction.Pattern,
                message,
                reaction.Replacement,
                out var command,
                out var matchError);
            if (matchStatus == ReactionMatchStatus.NoMatch || matchStatus == ReactionMatchStatus.TimedOut)
                return;
            if (matchStatus == ReactionMatchStatus.InvalidReplacement)
            {
                await NotifyReactionErrorAsync(reaction, matchError ?? "Invalid replacement pattern.", pluginToken);
                return;
            }

            var restartImmediately = PluginUiLogic.RestartsActiveRun(reaction.ExecutionPolicy);
            if (restartImmediately)
            {
                CancelQueuedRetriggers(reaction.Source);
                if (ActiveReactionCancellations.TryGetValue(reaction.Source, out var activeCancellation))
                {
                    try { activeCancellation.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
            }

            // While queued triggers are draining, a new trigger goes behind them, never ahead.
            if (!restartImmediately &&
                retriggerQueues.TryGetValue(reaction.Source, out var activeQueue) &&
                activeQueue.IsActive)
            {
                if (reaction.ExecutionPolicy != ReactionExecutionPolicy.IgnoreWhileRunning)
                    QueueRetrigger(reaction, command, pluginToken);
                else
                    await NotifySuppressionAsync(reaction, ReactionRejectionReason.Busy, pluginToken);
                return;
            }

            if (!ExecutionGate.TryEnter(
                    reaction.Source,
                    restartImmediately ? TimeSpan.Zero : TimeSpan.FromSeconds(reaction.CooldownSeconds),
                    Stopwatch.GetTimestamp(),
                    out var lease,
                    out var rejectionReason,
                    restartImmediately))
            {
                if (rejectionReason == ReactionRejectionReason.Busy &&
                    reaction.ExecutionPolicy != ReactionExecutionPolicy.IgnoreWhileRunning)
                {
                    QueueRetrigger(reaction, command, pluginToken);
                    return;
                }
                await NotifySuppressionAsync(reaction, rejectionReason, pluginToken);
                return;
            }

            await RunAcceptedCommandAsync(reaction, command, lease!, pluginToken, fromQueue: false);
        }

        private static async Task RunAcceptedCommandAsync(
            ReactionSnapshot reaction,
            string command,
            IDisposable lease,
            CancellationToken pluginToken,
            bool fromQueue)
        {
            // The lease is owned from the first line, so nothing below can leave the reaction stuck as busy.
            using (lease)
            {
                pluginToken.ThrowIfCancellationRequested();
                if (fromQueue)
                    ReactionVisualizerState.DequeuedRun(reaction.Control.VisualizerId);
                var visualizerRunId = ReactionVisualizerState.Started(
                    reaction.Control.VisualizerId,
                    reaction.Name,
                    command);
                var lines = MyRegex().Split(command);
                using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(pluginToken);
                ActiveReactionCancellations[reaction.Source] = runCancellation;
                try
                {
                    if (Volatile.Read(ref reaction.Control.Generation) != reaction.Generation)
                    {
                        runCancellation.Cancel();
                        return;
                    }
                    await RunMacroAsync(lines, reaction, runCancellation, pluginToken);
                }
                finally
                {
                    ActiveReactionCancellations.TryRemove(reaction.Source, out _);
                    ReactionVisualizerState.Finished(
                        visualizerRunId,
                        runCancellation.IsCancellationRequested,
                        reaction.Source.Enabled);
                }
            }
        }

        private static void QueueRetrigger(
            ReactionSnapshot reaction,
            string command,
            CancellationToken pluginToken)
        {
            var scheduler = retriggerQueues.GetValue(reaction.Source, source =>
                new BoundedRetriggerScheduler<PendingRetrigger>(
                    16,
                    (pending, cancellationToken) => ExecutionGate.EnterWhenAvailableAsync(
                        source,
                        PluginUiLogic.IgnoresCooldown(pending.Reaction.ExecutionPolicy)
                            ? TimeSpan.Zero
                            : TimeSpan.FromSeconds(pending.Reaction.CooldownSeconds),
                        cancellationToken,
                        PluginUiLogic.IgnoresCooldown(pending.Reaction.ExecutionPolicy)),
                    (pending, lease) => RunAcceptedCommandAsync(
                        pending.Reaction,
                        pending.Command,
                        lease,
                        pending.PluginToken,
                        fromQueue: true),
                    dropped => Interlocked.Add(ref droppedRetriggerCount, dropped),
                    (exception, discarded) =>
                    {
                        if (discarded > 0)
                            Interlocked.Add(ref droppedRetriggerCount, discarded);
                        Service.PluginLog.Error(
                            exception,
                            "Retrigger scheduler failed for {ReactionName}; discarded {DiscardedCount} pending trigger(s).",
                            source.Name,
                            discarded);
                        Track(NotifySchedulerFailureAsync(source, exception.Message, discarded, pluginToken));
                    }));
            var drainer = scheduler.Enqueue(
                reaction.ExecutionPolicy,
                new PendingRetrigger(reaction, command, pluginToken),
                pluginToken);
            ReactionVisualizerState.QueuedRun(
                reaction.Control.VisualizerId,
                reaction.Name,
                command,
                reaction.ExecutionPolicy);
            if (drainer != null)
                Track(drainer);
        }
        public static void OnChatMessage(IHandleableChatMessage message)
        {
            OnChatMessage(message.LogKind, message.Timestamp, message.Sender, message.Message, message.IsHandled);
        }
        public static void OnChatMessage(XivChatType type, int timestamp, SeString sender, SeString message, bool isHandled)
        {
            if (Volatile.Read(ref shuttingDown) != 0)
                return;
            if (Service.configuration!.DebugLogTypes)
            {
                var prefix = int.TryParse(type.ToString(), out var number)?"[" + number + "]":"[" + ((int)type) + "][" + type + "]";
                prefix += (sender.ToString().IsNullOrEmpty() ? "" : "<" + sender + "> ");
                DebugLogBuffer.Add((int)type, $"[{DateTime.Now:HH:mm:ss}] {prefix} {message}", message.ToString());
            }

            if (isHandled) return;

            try
            {
                // Follow mode first: a line it takes ("Ami follow") isn't also matched by the triggers.
                if (FollowMode.TryHandle(type, sender, message))
                    return;
                EnqueueMessage(type, sender, message);
            }
            catch (Exception ex)
            {
                Service.PluginLog.Error(ex, "PuppetMasterKK failed to process a chat message.");
            }
        }

        // Framework thread: the sender is resolved here, from the game's friend, FC and party lists.
        private static void EnqueueMessage(XivChatType type, SeString sender, SeString seMessage)
        {
            var message = ReactionCommandMatcher.SanitizeIncoming(seMessage.ToString());
            List<ReactionSnapshot>? snapshots = null;
            SenderInfo? senderInfo = null;
            var configuration = Service.configuration!;
            foreach (var reaction in configuration.Reactions)
            {
                if (!reaction.Enabled || !reaction.EnabledChannels.Contains((int)type))
                    continue;

                if (configuration.IgnoreOwnMessages || reaction.Senders?.NeedsSender == true)
                {
                    senderInfo ??= SenderResolver.FromChat(type, sender, seMessage);
                    if (configuration.IgnoreOwnMessages && senderInfo.Value.IsSelf)
                        return;
                    if (reaction.Senders != null && !reaction.Senders.Allows(senderInfo.Value))
                        continue;
                }

                var snapshot = CreateSnapshot(
                    reaction,
                    configuration.ShowReactionNotifications,
                    configuration.ShowSuppressedReactionNotifications);
                if (snapshot != null)
                    (snapshots ??= []).Add(snapshot);
            }
            if (snapshots == null)
                return;

            dispatcher.Writer.TryWrite(new ChatEnvelope(type, message, snapshots));
        }

        [GeneratedRegex("\r\n|\r|\n")]
        private static partial Regex MyRegex();
    }
}
