using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
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
    public class ChatHandler
    {
        private static readonly ReactionExecutionGate ExecutionGate = new();
        private static readonly ConcurrentDictionary<Reaction, ActiveRun> ActiveReactionCancellations =
            new(ReferenceEqualityComparer.Instance);
        private static readonly ConcurrentDictionary<IActiveNotification, byte> ActiveNotifications = new();
        private static ConditionalWeakTable<Reaction, SuppressionNotificationState> suppressionNotifications = new();
        private static ConditionalWeakTable<Reaction, ErrorNotificationState> errorNotifications = new();
        private static ConditionalWeakTable<Reaction, ReactionControlState> reactionControls = new();
        private static ConditionalWeakTable<Reaction, BoundedRetriggerScheduler<PendingRetrigger>> retriggerQueues = new();
        private static CancellationTokenSource pluginLifetime = new();
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
            HashSet<string> CommandWhitelist,
            HashSet<string> CommandBlacklist,
            Regex Pattern,
            string Replacement,
            bool[] TemplateWaitLines,
            ProtectionSettings Protections,
            bool NoProtections,
            bool Practice,
            ChoiceSet? Choices,
            bool[][] ChoiceWaitLines,
            int PerSenderCooldownSeconds,
            bool OneWaitingPerSender,
            string[] FinalCommands,
            bool[] FinalWaitLines,
            FinalActionWhen FinalWhen);

        private sealed record ChatEnvelope(string Message, SenderInfo Sender, List<ReactionSnapshot> Reactions);

        // One request: the commands it runs, which of their lines are the trigger's own /wait, and who sent it
        // (SenderCooldowns.KeyFor, "" when unknown).
        private sealed record RunRequest(string Command, bool[] WaitLines, string From);

        private sealed record PendingRetrigger(
            ReactionSnapshot Reaction,
            RunRequest Run,
            CancellationToken PluginToken);

        // The final action stops after this long.
        private static readonly TimeSpan FinalActionLimit = TimeSpan.FromSeconds(10);

        private sealed class SuppressionNotificationState
        {
            public int PendingCount;
            public long NextNotificationTimestamp;
        }

        private sealed class ReactionControlState
        {
            public long Generation;
            public long VisualizerId = Interlocked.Increment(ref nextVisualizerReactionId);
            public readonly ChoiceTurn Turn = new();
            public readonly SenderCooldowns SenderCooldowns = new();
        }

        // A running trigger's token source, and whether a newer request (Restart immediately) is what stopped it.
        private sealed class ActiveRun(CancellationTokenSource cancellation)
        {
            public readonly CancellationTokenSource Cancellation = cancellation;
            public int Interrupted;

            public void Cancel(bool interrupted = false)
            {
                if (interrupted)
                    Volatile.Write(ref Interrupted, 1);
                try { Cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }

        private sealed class ErrorNotificationState
        {
            public long NextNotificationTimestamp;
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
            foreach (var run in ActiveReactionCancellations.Values)
                run.Cancel();
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

        // Every trigger: running ones stop, waiting ones are dropped.
        public static void CancelAll(Configuration configuration)
        {
            foreach (var reaction in configuration.Reactions)
                CancelReaction(reaction);
        }

        public static void CancelReaction(Reaction reaction)
        {
            InvalidateReaction(reaction, true);
        }

        public static void InvalidateReaction(Reaction reaction, bool cancelActive)
        {
            var control = reactionControls.GetValue(reaction, static _ => new ReactionControlState());
            Interlocked.Increment(ref control.Generation);
            if (cancelActive && ActiveReactionCancellations.TryGetValue(reaction, out var run))
                run.Cancel();
            CancelQueuedRetriggers(reaction);
        }

        // Drops a trigger's waiting requests; replaced: a newer request took their place (counted and shown as such).
        private static void CancelQueuedRetriggers(Reaction reaction, bool replaced = false)
        {
            if (reactionControls.TryGetValue(reaction, out var control))
                ReactionVisualizerState.ClearQueued(control.VisualizerId, replaced);
            if (!retriggerQueues.TryGetValue(reaction, out var state))
                return;
            var cleared = state.Cancel();
            if (replaced && control != null)
                ReactionVisualizerState.Count(control.VisualizerId, VisualizerCounter.Replaced, cleared);
        }

        /// <summary>Practice mode on or off (this session). Everything running or waiting stops either way.</summary>
        public static void SetPracticeMode(Configuration configuration, bool on)
        {
            if (configuration.PracticeMode == on)
                return;
            configuration.PracticeMode = on;
            CancelAll(configuration);
        }

        public static long DroppedMessageCount => Interlocked.Read(ref droppedMessageCount);
        public static long DroppedRetriggerCount => Interlocked.Read(ref droppedRetriggerCount);

        internal static long GetVisualizerId(Reaction reaction)
        {
            return reactionControls.GetValue(reaction, static _ => new ReactionControlState()).VisualizerId;
        }

        /// <summary>In turn: the position of the trigger's next choice (for Try it and Test all triggers).</summary>
        internal static int GetChoiceTurn(Reaction reaction)
        {
            return reactionControls.GetValue(reaction, static _ => new ReactionControlState()).Turn.Current;
        }

        public static void ResetDroppedMessageCount()
        {
            Interlocked.Exchange(ref droppedMessageCount, 0);
            Interlocked.Exchange(ref droppedRetriggerCount, 0);
            ReactionVisualizerState.ResetCounters();
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
                        var task = DoCommandAsync(reaction, envelope.Message, envelope.Sender, token);
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
            bool showSuppressionNotifications,
            bool practice)
        {
            var pattern = ReactionCommandMatcher.SelectPattern(reaction);
            if (pattern == null)
                return null;
            var control = reactionControls.GetValue(reaction, static _ => new ReactionControlState());
            var replacement = ReactionCommandMatcher.SelectReplacement(reaction);
            var choices = ChoiceSet.From(reaction);
            var finalCommands = PluginUiLogic.FinalCommandLines(reaction);

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
                Service.Commands.CanonicalSet(reaction.CommandWhitelist),
                Service.Commands.CanonicalSet(reaction.CommandBlacklist),
                pattern,
                replacement,
                ReactionCommandMatcher.TemplateWaitLines(replacement),
                (reaction.Protections ?? new ProtectionSettings()).Clone(),
                reaction.NoProtections,
                practice,
                choices,
                choices?.Commands.Select(ReactionCommandMatcher.TemplateWaitLines).ToArray() ?? [],
                Math.Clamp(reaction.PerSenderCooldownSeconds, 0, Reaction.MaxPerSenderCooldownSeconds),
                reaction.OneWaitingPerSender,
                finalCommands,
                ReactionCommandMatcher.TemplateWaitLines(string.Join('\n', finalCommands)),
                reaction.FinalWhen);
        }

        // Fire-and-forget work: a failure is logged instead of going unobserved.
        private static void Track(Task task)
        {
            _ = task.ContinueWith(
                completed =>
                {
                    if (completed.Exception != null)
                        Service.PluginLog.Error(completed.Exception, "PuppetMasterKK background task failed.");
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        // True when every line was gone through (not stopped, not failed). waitLines: which lines are the trigger's own
        // /wait. notify: show the progress notification (not for the final action).
        private static async Task<bool> RunMacroAsync(
            string[] lines,
            bool[] waitLines,
            ReactionSnapshot reaction,
            long visualizerRunId,
            CancellationTokenSource cancellation,
            CancellationToken pluginToken,
            bool notify = true)
        {
            IActiveNotification? notification = null;
            var completed = false;

            if (notify && reaction.ShowNotifications && !cancellation.IsCancellationRequested)
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
                    var textCommand = ReactionCommandMatcher.FormatCommand(lines[lineIndex]);
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
                        if (CommandPolicy.IsWaitAllowed(catalog, ReactionCommandMatcher.IsTemplateWait(waitLines, lineIndex),
                                                        reaction.CommandWhitelist,
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
                                if (Service.IsCommandAllowed(textCommand.Main, reaction.CommandWhitelist,
                                                             reaction.CommandBlacklist, reaction.AllowAllCommands,
                                                             reaction.Protections, reaction.NoProtections,
                                                             out var permissionReason))
                                    return true;
                                Service.PluginLog.Debug("{Reaction}: {Command} blocked: {Reason}", reaction.Name, textCommand.Main, permissionReason);
                                ReactionVisualizerState.Count(reaction.Control.VisualizerId, VisualizerCounter.BlockedLines);
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

                                // Practice mode: everything up to here ran as usual (waits, checks, the rate limit); only
                                // the send is skipped.
                                if (reaction.Practice)
                                {
                                    if (!cancellation.IsCancellationRequested)
                                        ReactionVisualizerState.WouldSend(visualizerRunId, textCommand.ToString());
                                }
                                else await Service.Framework.RunOnFrameworkThread(() =>
                                {
                                    if (cancellation.IsCancellationRequested)
                                        return;
                                    try
                                    {
                                        GameChat.Send(textCommand.ToString());
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

                completed = !cancellation.IsCancellationRequested;
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
            return completed;
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

            var reasonText = reason switch
            {
                ReactionRejectionReason.Busy => "busy running or processing queued triggers",
                ReactionRejectionReason.SenderCooldown => "this person's cooldown is active",
                _ => "cooldown active",
            };
            var countText = suppressedCount > 1 ? $"\n{suppressedCount} repeated triggers suppressed." : string.Empty;
            try
            {
                await Service.Framework.RunOnFrameworkThread(() =>
                {
                    if (pluginToken.IsCancellationRequested)
                        return;
                    Service.Notify($"Trigger suppressed: {reaction.Name}\n{reasonText}.{countText}", NotificationType.Warning, 4);
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
                    Service.Notify($"Trigger failed: {reaction.Name}\n{error}", NotificationType.Error, 5);
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
                    Service.Notify($"Trigger scheduler failed: {reaction.Name}\n{error}" +
                                   (discarded > 0 ? $"\n{discarded} pending trigger(s) discarded." : string.Empty),
                                   NotificationType.Error, 6);
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

        private static async Task DoCommandAsync(ReactionSnapshot reaction, string message, SenderInfo sender,
                                                 CancellationToken pluginToken)
        {
            // Channels and senders were checked when the message came in; a later edit bumps the generation.
            if (pluginToken.IsCancellationRequested ||
                Volatile.Read(ref reaction.Control.Generation) != reaction.Generation)
                return;

            var matchStatus = ReactionCommandMatcher.TryGenerateCommand(
                reaction.Pattern,
                message,
                reaction.Replacement,
                reaction.Choices,
                reaction.Control.Turn.Current,
                null,
                out var command,
                out _,
                out var choice,
                out var matchError);
            if (matchStatus == ReactionMatchStatus.TimedOut)
                ReactionVisualizerState.Count(reaction.Control.VisualizerId, VisualizerCounter.TimedOut);
            if (matchStatus == ReactionMatchStatus.NoMatch || matchStatus == ReactionMatchStatus.TimedOut)
                return;
            if (matchStatus == ReactionMatchStatus.InvalidReplacement)
            {
                await NotifyReactionErrorAsync(reaction, matchError ?? "Invalid replacement pattern.", pluginToken);
                return;
            }

            var request = new RunRequest(
                command,
                choice >= 0 ? reaction.ChoiceWaitLines[choice] : reaction.TemplateWaitLines,
                SenderCooldowns.KeyFor(sender));

            // Someone still on their own cooldown is ignored before anything else: they can't restart or queue it.
            if (reaction.PerSenderCooldownSeconds > 0 &&
                reaction.Control.SenderCooldowns.IsWaiting(request.From, Stopwatch.GetTimestamp()))
            {
                await IgnoreAsync(reaction, ReactionRejectionReason.SenderCooldown, pluginToken);
                return;
            }

            var restartImmediately = PluginUiLogic.RestartsActiveRun(reaction.ExecutionPolicy);
            if (restartImmediately)
            {
                CancelQueuedRetriggers(reaction.Source, replaced: true);
                if (ActiveReactionCancellations.TryGetValue(reaction.Source, out var activeRun))
                    activeRun.Cancel(interrupted: true);
            }

            // While queued triggers are draining, a new trigger goes behind them, never ahead.
            if (!restartImmediately &&
                retriggerQueues.TryGetValue(reaction.Source, out var activeQueue) &&
                activeQueue.IsActive)
            {
                if (reaction.ExecutionPolicy != ReactionExecutionPolicy.IgnoreWhileRunning)
                    QueueRetrigger(reaction, request, pluginToken);
                else
                    await IgnoreAsync(reaction, ReactionRejectionReason.Busy, pluginToken);
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
                    QueueRetrigger(reaction, request, pluginToken);
                    return;
                }
                await IgnoreAsync(reaction, rejectionReason, pluginToken);
                return;
            }

            Accepted(reaction, request);
            await RunAcceptedCommandAsync(reaction, request, lease!, pluginToken, fromQueue: false);
        }

        // A request that runs or waits (not one that's ignored): In turn moves to the next choice, and the sender's own
        // cooldown starts.
        private static void Accepted(ReactionSnapshot reaction, RunRequest request)
        {
            if (reaction.Choices != null)
                reaction.Control.Turn.Advance();
            if (reaction.PerSenderCooldownSeconds > 0)
                reaction.Control.SenderCooldowns.Start(request.From, Stopwatch.GetTimestamp(),
                                                       reaction.PerSenderCooldownSeconds * Stopwatch.Frequency);
        }

        private static Task IgnoreAsync(ReactionSnapshot reaction, ReactionRejectionReason reason, CancellationToken pluginToken)
        {
            ReactionVisualizerState.Count(reaction.Control.VisualizerId,
                reason == ReactionRejectionReason.Busy ? VisualizerCounter.IgnoredBusy : VisualizerCounter.IgnoredCooldown);
            return NotifySuppressionAsync(reaction, reason, pluginToken);
        }

        private static async Task RunAcceptedCommandAsync(
            ReactionSnapshot reaction,
            RunRequest request,
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
                    request.Command,
                    reaction.Practice);
                var lines = ReactionCommandMatcher.SplitLines(request.Command);
                using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(pluginToken);
                var activeRun = new ActiveRun(runCancellation);
                ActiveReactionCancellations[reaction.Source] = activeRun;
                try
                {
                    if (Volatile.Read(ref reaction.Control.Generation) != reaction.Generation)
                    {
                        runCancellation.Cancel();
                        return;
                    }
                    if (await RunMacroAsync(lines, request.WaitLines, reaction, visualizerRunId, runCancellation, pluginToken))
                        await RunFinalActionAsync(reaction, visualizerRunId, pluginToken);
                }
                finally
                {
                    ActiveReactionCancellations.TryRemove(reaction.Source, out _);
                    ReactionVisualizerState.Finished(
                        visualizerRunId,
                        runCancellation.IsCancellationRequested,
                        reaction.Source.Enabled,
                        Volatile.Read(ref activeRun.Interrupted) != 0);
                }
            }
        }

        // The final action, still holding the trigger's lease: only after a run that finished on its own, never after
        // Stop, turning it off, /pmkk off, a restart or shutdown (each of those bumps the generation or cancels). Stop
        // and Restart immediately stop it too, and it stops by itself after FinalActionLimit.
        private static async Task RunFinalActionAsync(ReactionSnapshot reaction, long visualizerRunId, CancellationToken pluginToken)
        {
            if (reaction.FinalCommands.Length == 0 || pluginToken.IsCancellationRequested || !reaction.Source.Enabled ||
                Volatile.Read(ref reaction.Control.Generation) != reaction.Generation)
                return;
            if (reaction.FinalWhen == FinalActionWhen.WhenNothingWaiting &&
                retriggerQueues.TryGetValue(reaction.Source, out var queue) && queue.PendingCount > 0)
                return;

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(pluginToken);
            cancellation.CancelAfter(FinalActionLimit);
            ActiveReactionCancellations[reaction.Source] = new ActiveRun(cancellation);
            // Stopped between the check above and being registered: that Stop couldn't reach this token.
            if (Volatile.Read(ref reaction.Control.Generation) != reaction.Generation)
                return;
            await RunMacroAsync(reaction.FinalCommands, reaction.FinalWaitLines, reaction, visualizerRunId, cancellation,
                                pluginToken, notify: false);
        }

        private static void QueueRetrigger(
            ReactionSnapshot reaction,
            RunRequest request,
            CancellationToken pluginToken)
        {
            Accepted(reaction, request);
            var visualizerId = reaction.Control.VisualizerId;
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
                        pending.Run,
                        lease,
                        pending.PluginToken,
                        fromQueue: true),
                    dropped =>
                    {
                        Interlocked.Add(ref droppedRetriggerCount, dropped);
                        ReactionVisualizerState.Count(visualizerId, VisualizerCounter.DiscardedFull, dropped);
                    },
                    (exception, discarded) =>
                    {
                        if (discarded > 0)
                        {
                            Interlocked.Add(ref droppedRetriggerCount, discarded);
                            ReactionVisualizerState.Count(visualizerId, VisualizerCounter.DiscardedFull, discarded);
                        }
                        Service.PluginLog.Error(
                            exception,
                            "Retrigger scheduler failed for {ReactionName}; discarded {DiscardedCount} pending trigger(s).",
                            source.Name,
                            discarded);
                        Track(NotifySchedulerFailureAsync(source, exception.Message, discarded, pluginToken));
                    },
                    replaced => ReactionVisualizerState.Count(visualizerId, VisualizerCounter.Replaced, replaced)));
            // One waiting request per person (Queue every): the sender's newer request replaces their older one.
            var replaceSameSender = reaction.OneWaitingPerSender &&
                                    reaction.ExecutionPolicy == ReactionExecutionPolicy.QueueEveryTrigger;
            var from = request.From;
            var drainer = scheduler.Enqueue(
                reaction.ExecutionPolicy,
                new PendingRetrigger(reaction, request, pluginToken),
                pluginToken,
                replaceSameSender ? pending => pending.Run.From.Equals(from, StringComparison.OrdinalIgnoreCase) : null);
            ReactionVisualizerState.QueuedRun(
                reaction.Control.VisualizerId,
                reaction.Name,
                request.Command,
                reaction.ExecutionPolicy,
                from,
                replaceSameSender);
            if (drainer != null)
                Track(drainer);
        }

        // Game event: nothing may escape it.
        public static void OnChatMessage(IHandleableChatMessage chatMessage)
        {
            if (Volatile.Read(ref shuttingDown) != 0)
                return;
            try
            {
                var type = chatMessage.LogKind;
                var sender = chatMessage.Sender;
                var message = chatMessage.Message;
                if (Service.configuration!.DebugLogTypes)
                {
                    var prefix = int.TryParse(type.ToString(), out var number)?"[" + number + "]":"[" + ((int)type) + "][" + type + "]";
                    prefix += (sender.ToString().IsNullOrEmpty() ? "" : "<" + sender + "> ");
                    DebugLogBuffer.Add((int)type, $"[{DateTime.Now:HH:mm:ss}] {prefix} {message}", message.ToString());
                }

                if (chatMessage.IsHandled)
                    return;

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

                // Per-person limits need to know who it was too.
                if (configuration.IgnoreOwnMessages || reaction.Senders?.NeedsSender == true ||
                    reaction.PerSenderCooldownSeconds > 0 || reaction.OneWaitingPerSender)
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
                    configuration.ShowSuppressedReactionNotifications,
                    configuration.PracticeMode);
                if (snapshot != null)
                    (snapshots ??= []).Add(snapshot);
            }
            if (snapshots == null)
                return;

            dispatcher.Writer.TryWrite(new ChatEnvelope(message, senderInfo ?? SenderInfo.Unknown, snapshots));
        }
    }
}
