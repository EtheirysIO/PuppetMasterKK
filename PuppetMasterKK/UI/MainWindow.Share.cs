using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using phys1ksUI;

namespace PuppetMasterKK.UI;

// Share codes: copy a trigger as text, and import one after reviewing everything it would let through.
internal sealed partial class MainWindow
{
    private bool importOpen;
    private string? importError;
    private ShareCodeReview? importReview;
    private Reaction? importCandidate;
    private string importTest = string.Empty;
    private ReactionPreview importPreview = ReactionPreview.Empty;
    private bool importDirty;
    private long importPreviewAt;

    private static void CopyShareCode(Reaction reaction)
    {
        if (!ShareCodeCodec.TryEncode(reaction, IsOfficialChannel, Config.MaxRegexLength, out var code, out var protectionsOn,
                                      out var error))
        {
            Service.Notify(error, NotificationType.Error, 6);
            return;
        }
        ImGui.SetClipboardText(code);
        if (protectionsOn)
            Service.Notify("Share code copied. This trigger has no protections, so the code has them turned on.",
                           NotificationType.Warning, 8);
        else
            Service.Notify("Share code copied.", NotificationType.Success, 4);
    }

    private void OpenImport()
    {
        ReadImportCode();
        importOpen = true;
    }

    // The clipboard only: codes are never taken from chat.
    private void ReadImportCode()
    {
        importReview = null;
        importCandidate = null;
        string? text;
        try
        {
            text = ImGui.GetClipboardText();
        }
        catch (Exception)
        {
            text = null;
        }
        if (!ShareCodeCodec.TryDecode(text, Config.MaxRegexLength, out var share, out var error))
        {
            importError = error;
            return;
        }
        importError = null;
        importReview = new ShareCodeReview(share, Config, Service.Commands, Service.IsPluginCommand, IsOfficialChannel);
        // Never prefilled from the code: a pattern built to stall on its own message would hitch the window.
        importTest = string.Empty;
        importPreview = ReactionPreview.Empty;
        importDirty = true;
    }

    private static string NoId(string text) => PluginUiLogic.NoId(text);

    private void DrawImportDialog()
    {
        if (!importOpen)
            return;
        Modal.Draw("Import trigger###importTrigger", ref importOpen, DrawImportBody);
        if (!importOpen)
        {
            importReview = null;
            importCandidate = null;
            importError = null;
        }
    }

    private void DrawImportBody()
    {
        if (importReview is not { } review)
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26f);
            ImGui.TextColored(Theme.Negative, importError ?? "The clipboard doesn't hold a PuppetMasterKK share code.");
            ImGui.TextColored(Theme.Dim, "Copy a share code, then press Paste again.");
            ImGui.PopTextWrapPos();
            Gap(6f);
            if (W.PrimaryButton("Paste again##importRetry"))
                ReadImportCode();
            ImGui.SameLine();
            if (W.SecondaryButton("Cancel##importCancel"))
                Modal.Close(ref importOpen);
            return;
        }

        if (importDirty || importCandidate == null)
        {
            importCandidate = review.Build();
            ReactionCommandMatcher.CompilePatterns(importCandidate, Config.MaxRegexLength);
        }
        var candidate = importCandidate;
        // Other plugins' commands can come and go: refreshed every couple of seconds too, but not after the pattern
        // timed out or failed (that only changes with the message).
        if (importDirty || (importPreview.Status != PreviewStatus.Error && Environment.TickCount64 - importPreviewAt > 2000))
        {
            importPreview = PluginUiLogic.BuildPreview(candidate, importTest, Service.Commands.IsEmote, Service.IsCommandAllowed,
                                                       0, PreviewRandom(importTest));
            importPreviewAt = Environment.TickCount64;
            importDirty = false;
        }

        if (ImGui.BeginChild("##importScroll", new Vector2(Theme.S(560f), Theme.S(420f)), false))
        {
            DrawImportSummary(review, candidate);
            DrawImportTry(candidate);
            DrawImportRisks(review);
            DrawImportChannels(review, candidate);
        }
        ImGui.EndChild();

        W.Divider(Theme.S(6f));
        if (W.PrimaryButton("Add trigger##importAdd", tooltip: "It's added turned off"))
        {
            var reaction = review.Build();
            Config.Reactions.Add(reaction);
            Select(Config.Reactions.Count - 1);
            page = Page.Reactions;
            Service.Notify($"Added \"{DisplayName(reaction)}\". It's off until you turn it on.", NotificationType.Success, 6);
            Modal.Close(ref importOpen);
        }
        ImGui.SameLine();
        if (W.SecondaryButton("Cancel##importCancel"))
            Modal.Close(ref importOpen);
    }

    private static void DrawImportSummary(ShareCodeReview review, Reaction candidate)
    {
        using (W.Card("importTrigger", NoId(DisplayName(candidate)), candidate.UseRegex ? "Regex pattern" : "Phrase"))
        {
            W.TextWrapped(candidate.UseRegex ? candidate.CustomPhrase : candidate.TriggerPhrase, Theme.Ink);
            if (ChoiceSelector.IsActive(candidate))
            {
                for (var i = 0; i < candidate.Choices.Count; i++)
                {
                    var choice = candidate.Choices[i];
                    Gap(2f);
                    Label(candidate.ChoiceMode == ChoiceMode.ByWord ? $"Choice {i + 1}: \"{choice.Word}\"" : $"Choice {i + 1}");
                    W.TextWrapped(choice.Commands, Theme.Ink);
                }
            }
            else if (candidate.UseRegex)
            {
                Gap(2f);
                Label("Commands to run");
                W.TextWrapped(candidate.ReplaceMatch, Theme.Ink);
            }
            if (review.Allowed.Count > 0)
            {
                Gap(2f);
                Label("Allowed in the code");
                W.TextWrapped(string.Join(", ", review.Allowed), Theme.Ink);
            }
            if (review.LookAlikes.Count > 0)
            {
                Gap(2f);
                Label("Left out: look-alikes");
                W.TextWrapped(string.Join(", ", review.LookAlikes) + ". Written to pass as another command, so they're never imported.",
                              Theme.Negative);
            }
            var final = PluginUiLogic.FinalCommandLines(candidate);
            if (final.Length > 0)
            {
                Gap(2f);
                Label("Final action");
                W.TextWrapped(string.Join('\n', final), Theme.Ink);
            }
            Gap(2f);
            Hint($"It's added turned off, for {candidate.Senders.Describe()} (like your new triggers). Who could trigger it before isn't shared.");
        }
    }

    private void DrawImportTry(Reaction candidate)
    {
        using (W.Card("importTry", "Try it"))
        {
            if (W.TextInput("##importTest", ref importTest, "Type a chat message", 0f, 500))
                importDirty = true;
            if (string.IsNullOrWhiteSpace(importTest))
            {
                Hint("See what a message would run under your rules and what you tick below.");
                return;
            }
            Gap(2f);
            DrawPreview(importPreview, candidate.UseRegex);
        }
    }

    private void DrawImportRisks(ShareCodeReview review)
    {
        if (review.Risks.Count == 0)
            return;
        var ticked = review.Risks.FindAll(risk => risk.Accepted).Count;
        using (W.Card("importRisks", "Needs your OK", $"{ticked} of {review.Risks.Count} ticked"))
        {
            Hint("This trigger allows more than your new triggers do. Anything left unticked is left out.");
            Gap(2f);
            for (var i = 0; i < review.Risks.Count; i++)
            {
                var risk = review.Risks[i];
                var accepted = risk.Accepted;
                if (W.Checkbox($"{NoId(risk.Label)}##risk{i}", ref accepted, tooltip: risk.Detail))
                {
                    risk.Accepted = accepted;
                    importDirty = true;
                }
                ImGui.SameLine(0f, Theme.Space.Tight);
                ImGui.TextColored(Theme.Faint, W.Fit(risk.Detail, W.Avail()));
            }
        }
    }

    private void DrawImportChannels(ShareCodeReview review, Reaction candidate)
    {
        using (W.Card("importChannels", "Channels", candidate.EnabledChannels.Count == 1 ? "1 picked" : $"{candidate.EnabledChannels.Count} picked"))
        {
            DrawChannelChips(candidate.EnabledChannels, "No channels yet. Pick them after adding it.");
            if (review.SuggestedChannels.Count == 0)
                return;
            Gap();
            Label("It also listened to");
            foreach (var channel in review.SuggestedChannels)
            {
                var accepted = channel.Accepted;
                var label = ChannelName(channel.Id) + (channel.IsPublic ? " (public: strangers talk here)" : string.Empty);
                if (W.Checkbox($"{label}##importChannel{channel.Id}", ref accepted, tooltip: $"Log type {channel.Id}"))
                {
                    channel.Accepted = accepted;
                    importDirty = true;
                }
            }
        }
    }
}
