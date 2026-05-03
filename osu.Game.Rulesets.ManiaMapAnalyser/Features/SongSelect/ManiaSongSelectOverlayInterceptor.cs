using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.Toolbar;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.ManiaMapAnalyser.Configuration;
using osu.Game.Rulesets.ManiaMapAnalyser.Features.Injection;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osuTK;
using osuTK.Graphics;
using OsuSongSelect = osu.Game.Screens.Select.SongSelect;

namespace osu.Game.Rulesets.ManiaMapAnalyser.Features.SongSelect;

public partial class ManiaSongSelectOverlayInterceptor : AbstractHandler
{
    private const int config_retry_attempts = 30;
    private const double config_retry_delay = 250;

    private static readonly BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly PropertyInfo? internalChildrenProperty = typeof(CompositeDrawable).GetProperty("InternalChildren", flags);
    private static readonly EventInfo? childBecameAliveEvent = typeof(CompositeDrawable).GetEvent("ChildBecameAlive", flags);
    private static readonly EventInfo? childDiedEvent = typeof(CompositeDrawable).GetEvent("ChildDied", flags);
    private static readonly FieldInfo? activationRequestedField = typeof(TabItem<RulesetInfo>).GetField("ActivationRequested", flags);
    private static readonly FieldInfo? mainContentField = typeof(osu.Game.Screens.Select.SongSelect).GetField("mainContent", flags);

    private readonly Dictionary<CompositeDrawable, CompositeObserver> compositeObservers = new();
    private readonly Dictionary<OsuSongSelect, SongSelectPatchState> patchedSongSelects = new();
    private readonly Dictionary<ToolbarRulesetTabButton, ToolbarPatchState> patchedToolbarButtons = new();

    [Resolved(canBeNull: true)]
    private IRulesetConfigCache? rulesetConfigCache { get; set; }

    private ManiaMapAnalyserRulesetConfigManager? analyserConfig;
    private bool overlayEnabled = true;

    protected override void LoadComplete()
    {
        base.LoadComplete();
        ManiaMapAnalyserOverlayRuntime.OverlayEnabled.BindValueChanged(v =>
        {
            overlayEnabled = v.NewValue;
            handleOverlayEnabledChanged();
        }, true);

        trySyncRuntimeFromConfig();
        attachSubtree(Game);
    }

    private void attachSubtree(Drawable root)
    {
        var stack = new Stack<Drawable>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            Drawable current = stack.Pop();
            inspectDrawable(current);

            if (current is not CompositeDrawable composite)
                continue;

            ensureCompositeObserver(composite);
            pushChildren(composite, stack);
        }
    }

    private void detachSubtree(Drawable root)
    {
        var stack = new Stack<Drawable>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            Drawable current = stack.Pop();

            if (current is CompositeDrawable composite)
            {
                removeCompositeObserver(composite);
                pushChildren(composite, stack);
            }

            cleanupDrawable(current);
        }
    }

    private void inspectDrawable(Drawable drawable)
    {
        if (drawable is OsuSongSelect songSelect)
            Schedule(() => patchSongSelect(songSelect));

        if (drawable is ToolbarRulesetTabButton toolbarButton)
            Schedule(() => patchToolbarButton(toolbarButton));
    }

    private void cleanupDrawable(Drawable drawable)
    {
        if (drawable is OsuSongSelect songSelect)
            restoreSongSelectPatch(songSelect);

        if (drawable is ToolbarRulesetTabButton toolbarButton)
            restoreToolbarButton(toolbarButton);
    }

    private void onChildBecameAlive(Drawable child) => attachSubtree(child);

    private void onChildDied(Drawable child) => detachSubtree(child);

    private void ensureCompositeObserver(CompositeDrawable composite)
    {
        if (compositeObservers.ContainsKey(composite))
            return;

        compositeObservers[composite] = new CompositeObserver(composite, onChildBecameAlive, onChildDied);
    }

    private void removeCompositeObserver(CompositeDrawable composite)
    {
        if (!compositeObservers.Remove(composite, out CompositeObserver? observer))
            return;

        observer.Dispose();
    }

    private void patchSongSelect(OsuSongSelect songSelect)
    {
        try
        {
            if (patchedSongSelects.ContainsKey(songSelect))
                return;

            if (mainContentField?.GetValue(songSelect) is not Container overlayHost)
            {
                Logger.Log($"[ManiaMapAnalyser] Failed to find a valid overlay host for {songSelect.GetType().Name}.");
                return;
            }

            var overlay = new ManiaSongSelectOverlay();
            overlay.SetOverlayEnabled(overlayEnabled);

            overlayHost.Add(overlay);
            patchedSongSelects[songSelect] = new SongSelectPatchState(overlay);
        }
        catch (Exception ex)
        {
            ManiaMapAnalyserLogging.Error(ex, $"Failed while patching {songSelect.GetType().Name}.");
        }
    }

    private void restoreSongSelectPatch(OsuSongSelect songSelect)
    {
        if (!patchedSongSelects.Remove(songSelect, out SongSelectPatchState? state))
            return;

        state.Dispose();
    }

    private void patchToolbarButton(ToolbarRulesetTabButton toolbarButton)
    {
        try
        {
            if (patchedToolbarButtons.ContainsKey(toolbarButton))
                return;

            if (!string.Equals(toolbarButton.Value.ShortName, ManiaMapAnalyserRuleset.RULESET_SHORT_NAME, StringComparison.Ordinal))
                return;

            patchedToolbarButtons[toolbarButton] = new ToolbarPatchState
            {
                WasEnabled = toolbarButton.Enabled.Value,
                OriginalActivationRequested = activationRequestedField?.GetValue(toolbarButton),
            };

            toolbarButton.Enabled.Value = false;
            activationRequestedField?.SetValue(toolbarButton, null);
        }
        catch (Exception ex)
        {
            ManiaMapAnalyserLogging.Error(ex, "Failed while patching the toolbar button.");
        }
    }

    private void restoreToolbarButton(ToolbarRulesetTabButton toolbarButton)
    {
        if (!patchedToolbarButtons.Remove(toolbarButton, out ToolbarPatchState? state))
            return;

        toolbarButton.Enabled.Value = state.WasEnabled;
        activationRequestedField?.SetValue(toolbarButton, state.OriginalActivationRequested);
    }

    private ManiaMapAnalyserRulesetConfigManager? tryGetAnalyserConfig()
    {
        if (rulesetConfigCache == null)
            return null;

        try
        {
            return rulesetConfigCache.GetConfigFor(new ManiaMapAnalyserRuleset()) as ManiaMapAnalyserRulesetConfigManager;
        }
        catch (Exception ex)
        {
            ManiaMapAnalyserLogging.Error(ex, "Failed to resolve the ruleset config cache entry.");
            return null;
        }
    }

    private void trySyncRuntimeFromConfig(int attempt = 0)
    {
        analyserConfig ??= tryGetAnalyserConfig();

        if (analyserConfig == null)
        {
            if (attempt < config_retry_attempts)
                Scheduler.AddDelayed(() => trySyncRuntimeFromConfig(attempt + 1), config_retry_delay);

            return;
        }

        ManiaMapAnalyserOverlayRuntime.SetOverlayEnabled(analyserConfig.GetBindable<bool>(ManiaMapAnalyserSetting.OverlayEnabled).Value);
        ManiaMapAnalyserOverlayRuntime.SetOverlayPositionX(analyserConfig.GetBindable<float>(ManiaMapAnalyserSetting.OverlayPositionX).Value);
        ManiaMapAnalyserOverlayRuntime.SetOverlayPositionY(analyserConfig.GetBindable<float>(ManiaMapAnalyserSetting.OverlayPositionY).Value);
        ManiaMapAnalyserOverlayRuntime.SetOverlayOpacity(analyserConfig.GetBindable<float>(ManiaMapAnalyserSetting.OverlayOpacity).Value);
        ManiaMapAnalyserOverlayRuntime.SetTextSize(analyserConfig.GetBindable<float>(ManiaMapAnalyserSetting.TextSize).Value);
        ManiaMapAnalyserOverlayRuntime.SetTextColourHex(analyserConfig.GetBindable<string>(ManiaMapAnalyserSetting.TextColourHex).Value);
        ManiaMapAnalyserOverlayRuntime.SetBackgroundColourHex(analyserConfig.GetBindable<string>(ManiaMapAnalyserSetting.BackgroundColourHex).Value);
        ManiaMapAnalyserOverlayRuntime.SetContentPadding(analyserConfig.GetBindable<float>(ManiaMapAnalyserSetting.ContentPadding).Value);
    }

    private void handleOverlayEnabledChanged()
    {
        if (!overlayEnabled)
        {
            foreach (SongSelectPatchState state in patchedSongSelects.Values)
                state.Overlay.SetOverlayEnabled(false);

            return;
        }

        foreach (OsuSongSelect songSelect in patchedSongSelects.Keys.ToList())
        {
            restoreSongSelectPatch(songSelect);
            patchSongSelect(songSelect);
        }

        foreach (SongSelectPatchState state in patchedSongSelects.Values)
            state.Overlay.SetOverlayEnabled(true);

        if (patchedSongSelects.Count == 0)
            attachSubtree(Game);
    }

    private static void pushChildren(CompositeDrawable composite, Stack<Drawable> stack)
    {
        if (internalChildrenProperty?.GetValue(composite) is not IEnumerable children)
            return;

        foreach (object? child in children)
        {
            if (child is Drawable drawable)
                stack.Push(drawable);
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        foreach (CompositeObserver observer in compositeObservers.Values)
            observer.Dispose();

        compositeObservers.Clear();

        foreach (OsuSongSelect songSelect in patchedSongSelects.Keys.ToList())
            restoreSongSelectPatch(songSelect);

        foreach (ToolbarRulesetTabButton toolbarButton in patchedToolbarButtons.Keys.ToList())
            restoreToolbarButton(toolbarButton);
    }

    private sealed class SongSelectPatchState : IDisposable
    {
        public readonly ManiaSongSelectOverlay Overlay;

        public SongSelectPatchState(ManiaSongSelectOverlay overlay)
        {
            Overlay = overlay;
        }

        public void Dispose() => Overlay.Expire();
    }

    private sealed class ToolbarPatchState
    {
        public bool WasEnabled;
        public object? OriginalActivationRequested;
    }

    private sealed class CompositeObserver : IDisposable
    {
        private readonly CompositeDrawable composite;
        private readonly Delegate? childBecameAliveHandler;
        private readonly Delegate? childDiedHandler;

        public CompositeObserver(CompositeDrawable composite, Action<Drawable> childBecameAlive, Action<Drawable> childDied)
        {
            this.composite = composite;
            childBecameAliveHandler = createHandler(childBecameAliveEvent, childBecameAlive);
            childDiedHandler = createHandler(childDiedEvent, childDied);

            addHandler(childBecameAliveEvent, childBecameAliveHandler);
            addHandler(childDiedEvent, childDiedHandler);
        }

        public void Dispose()
        {
            removeHandler(childBecameAliveEvent, childBecameAliveHandler);
            removeHandler(childDiedEvent, childDiedHandler);
        }

        private Delegate? createHandler(EventInfo? eventInfo, Action<Drawable> callback)
        {
            if (eventInfo?.EventHandlerType == null)
                return null;

            return Delegate.CreateDelegate(eventInfo.EventHandlerType, callback.Target, callback.Method, false);
        }

        private void addHandler(EventInfo? eventInfo, Delegate? handler)
        {
            if (eventInfo?.GetAddMethod(true) == null || handler == null)
                return;

            eventInfo.GetAddMethod(true)!.Invoke(composite, new object?[] { handler });
        }

        private void removeHandler(EventInfo? eventInfo, Delegate? handler)
        {
            if (eventInfo?.GetRemoveMethod(true) == null || handler == null)
                return;

            eventInfo.GetRemoveMethod(true)!.Invoke(composite, new object?[] { handler });
        }
    }

    private partial class ManiaSongSelectOverlay : CompositeDrawable
    {
        private const double analysis_debounce_delay = 350;

        private static readonly Color4 default_background_colour = new(18, 22, 31, 210);
        private static readonly Color4 default_text_colour = new(210, 219, 235, 255);

        private bool overlayEnabled = true;
        private readonly BindableFloat overlayPositionX = new();
        private readonly BindableFloat overlayPositionY = new();
        private readonly BindableFloat overlayOpacity = new();
        private readonly BindableFloat textSize = new();
        private readonly Bindable<string> textColourHex = new();
        private readonly Bindable<string> backgroundColourHex = new();
        private readonly BindableFloat contentPadding = new();
        private readonly IBindable<WorkingBeatmap> beatmap = new Bindable<WorkingBeatmap>();
        private readonly IBindable<IReadOnlyList<Mod>> mods = new Bindable<IReadOnlyList<Mod>>(Array.Empty<Mod>());

        private Container cardContainer = null!;
        private Box background = null!;
        private FillFlowContainer textFlow = null!;
        private OsuSpriteText difficultyText = null!;
        private readonly SemaphoreSlim analysisSemaphore = new(1, 1);
        private ModSettingChangeTracker? modSettingChangeTracker;
        private ScheduledDelegate? scheduledAnalysis;
        private long latestAnalysisRequest;

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> beatmapSource { get; set; } = null!;

        [Resolved]
        private IBindable<IReadOnlyList<Mod>> modsSource { get; set; } = null!;

        public override bool HandlePositionalInput => false;

        public ManiaSongSelectOverlay()
        {
            RelativeSizeAxes = Axes.Both;
            Depth = float.MinValue;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Child = new Container
                {
                    RelativePositionAxes = Axes.Both,
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Position = new Vector2(0.72f, 0.17f),
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 14,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = default_background_colour,
                        }.With(b => background = b),
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 6),
                            Padding = new MarginPadding(16),
                            Children = new Drawable[]
                            {
                                new OsuSpriteText
                                {
                                    Text = "-",
                                    AllowMultiline = false,
                                    Font = OsuFont.TorusAlternate.With(size: 18, weight: FontWeight.Bold),
                                    Colour = default_text_colour,
                                }.With(t => difficultyText = t),
                            }
                        }.With(f => textFlow = f)
                    }
                }.With(c => cardContainer = c)
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            overlayPositionX.BindTo(ManiaMapAnalyserOverlayRuntime.OverlayPositionX);
            overlayPositionY.BindTo(ManiaMapAnalyserOverlayRuntime.OverlayPositionY);
            overlayOpacity.BindTo(ManiaMapAnalyserOverlayRuntime.OverlayOpacity);
            textSize.BindTo(ManiaMapAnalyserOverlayRuntime.TextSize);
            textColourHex.BindTo(ManiaMapAnalyserOverlayRuntime.TextColourHex);
            backgroundColourHex.BindTo(ManiaMapAnalyserOverlayRuntime.BackgroundColourHex);
            contentPadding.BindTo(ManiaMapAnalyserOverlayRuntime.ContentPadding);
            beatmap.BindTo(beatmapSource);
            mods.BindTo(modsSource);

            overlayPositionX.BindValueChanged(_ => updateTransform(), true);
            overlayPositionY.BindValueChanged(_ => updateTransform(), true);
            overlayOpacity.BindValueChanged(_ => updateTransform(), true);
            textSize.BindValueChanged(_ => updateAppearance(), true);
            textColourHex.BindValueChanged(_ => updateAppearance(), true);
            backgroundColourHex.BindValueChanged(_ => updateAppearance(), true);
            contentPadding.BindValueChanged(_ => updateAppearance(), true);

            ruleset.BindValueChanged(r =>
            {
                updateVisibility(r.NewValue);
                queueAnalysis();
            }, true);

            beatmap.BindValueChanged(_ => queueAnalysis(), true);
            mods.BindValueChanged(modsChanged =>
            {
                refreshModSettingTracking(modsChanged.NewValue);
                queueAnalysis();
            }, true);
        }

        public void SetOverlayEnabled(bool enabled)
        {
            overlayEnabled = enabled;
            if (!IsLoaded)
                return;

            Schedule(() =>
            {
                updateVisibility(ruleset.Value);
                queueAnalysis();
            });
        }

        private void updateVisibility(RulesetInfo currentRuleset)
        {
            bool visible = overlayEnabled && string.Equals(currentRuleset.ShortName, ManiaMapAnalyserRuleset.MANIA_SHORT_NAME, StringComparison.OrdinalIgnoreCase);
            this.FadeTo(visible ? 1f : 0f, 200, Easing.OutQuint);
        }

        private void updateTransform()
        {
            cardContainer.Position = new Vector2(overlayPositionX.Value, overlayPositionY.Value);
            cardContainer.Alpha = overlayOpacity.Value;
        }

        private void updateAppearance()
        {
            float resolvedPadding = contentPadding.Value;

            var font = OsuFont.TorusAlternate.With(size: textSize.Value, weight: FontWeight.Bold);

            difficultyText.Font = font;

            if (tryParseRgbaHex(textColourHex.Value, out Color4 resolvedTextColour))
                difficultyText.Colour = resolvedTextColour;

            if (tryParseRgbaHex(backgroundColourHex.Value, out Color4 resolvedBackgroundColour))
                background.Colour = resolvedBackgroundColour;

            textFlow.Padding = new MarginPadding
            {
                Top = resolvedPadding,
                Bottom = resolvedPadding,
                Left = resolvedPadding,
                Right = resolvedPadding,
            };
        }

        private void queueAnalysis()
        {
            scheduledAnalysis?.Cancel();

            long requestId = Interlocked.Increment(ref latestAnalysisRequest);
            scheduledAnalysis = Scheduler.AddDelayed(() => beginAnalysis(requestId), analysis_debounce_delay);
        }

        private void refreshModSettingTracking(IReadOnlyList<Mod> currentMods)
        {
            modSettingChangeTracker?.Dispose();
            modSettingChangeTracker = null;

            if (currentMods.Count == 0)
                return;

            modSettingChangeTracker = new ModSettingChangeTracker(currentMods);
            modSettingChangeTracker.SettingChanged += _ => Schedule(queueAnalysis);
        }

        private void beginAnalysis(long requestId)
        {
            if (!shouldAnalyzeCurrentSelection())
            {
                setDisplayedDifficultyText("-");
                return;
            }

            ManiaMapAnalyserBeatmapAnalysis.AnalysisSnapshot? snapshot = ManiaMapAnalyserBeatmapAnalysis.TryCreate(beatmap.Value, mods.Value);
            if (snapshot == null)
            {
                setDisplayedDifficultyText("-");
                return;
            }

            _ = Task.Run(async () =>
            {
                string? resultText = null;
                Exception? error = null;

                await analysisSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (requestId != Interlocked.Read(ref latestAnalysisRequest))
                        return;

                    try
                    {
                        resultText = ManiaMapAnalyserBeatmapAnalysis.AnalyzeDifficultyText(snapshot);
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                }
                finally
                {
                    analysisSemaphore.Release();
                }

                if (requestId != Interlocked.Read(ref latestAnalysisRequest))
                    return;

                Schedule(() =>
                {
                    if (requestId != latestAnalysisRequest)
                        return;

                    if (error != null)
                    {
                        if (!shouldSuppressAnalysisError(error))
                            ManiaMapAnalyserLogging.Error(error, "Failed to analyze the selected beatmap.");

                        setDisplayedDifficultyText("-");
                        return;
                    }

                    setDisplayedDifficultyText(resultText ?? "-");
                });
            });
        }

        private bool shouldAnalyzeCurrentSelection()
            => overlayEnabled
               && string.Equals(ruleset.Value.ShortName, ManiaMapAnalyserRuleset.MANIA_SHORT_NAME, StringComparison.OrdinalIgnoreCase)
               && !beatmap.IsDefault
               && beatmap.Value != null;

        private void setDisplayedDifficultyText(string text)
            => difficultyText.Text = string.IsNullOrWhiteSpace(text) ? "-" : text.ReplaceLineEndings(" ");

        private static bool shouldSuppressAnalysisError(Exception error)
            => error is InvalidOperationException invalidOperationException
               && (string.Equals(invalidOperationException.Message, "Beatmap mode is not mania.", StringComparison.Ordinal)
                   || string.Equals(invalidOperationException.Message, "Beatmap parse failed.", StringComparison.Ordinal)
                   || string.Equals(invalidOperationException.Message, "Input JSON must provide beatmap.osuText.", StringComparison.Ordinal)
                   || string.Equals(invalidOperationException.Message, "Estimator failed to produce a valid result.", StringComparison.Ordinal));

        private static bool tryParseRgbaHex(string? rawValue, out Color4 colour)
        {
            colour = default_text_colour;

            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            string hex = rawValue.Trim();

            if (hex.StartsWith("#", StringComparison.Ordinal))
                hex = hex[1..];

            if (hex.Length == 6)
                hex += "FF";

            if (hex.Length != 8)
                return false;

            try
            {
                colour = new Color4(
                    Convert.ToByte(hex[0..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16),
                    Convert.ToByte(hex[6..8], 16));
                return true;
            }
            catch
            {
                return false;
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            scheduledAnalysis?.Cancel();
            modSettingChangeTracker?.Dispose();
            overlayPositionX.UnbindAll();
            overlayPositionY.UnbindAll();
            overlayOpacity.UnbindAll();
            textSize.UnbindAll();
            textColourHex.UnbindAll();
            backgroundColourHex.UnbindAll();
            contentPadding.UnbindAll();
            beatmap.UnbindAll();
            mods.UnbindAll();
        }
    }
}
