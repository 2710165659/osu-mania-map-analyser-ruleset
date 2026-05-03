using System;
using System.Collections.Generic;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.StateChanges;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Input.Handlers;
using osu.Game.Overlays.Settings;
using osu.Game.Replays;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.ManiaMapAnalyser.Configuration;
using osu.Game.Rulesets.ManiaMapAnalyser.Features.Injection;
using osu.Game.Rulesets.ManiaMapAnalyser.Graphics.Settings;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaMapAnalyser;

public partial class ManiaMapAnalyserRuleset : Ruleset
{
    public const string RULESET_SHORT_NAME = "maniamapanalyser";
    public const string MANIA_SHORT_NAME = "mania";

    public override string Description => "osu!mania map analyser";

    public override string ShortName => RULESET_SHORT_NAME;

    public override string PlayingVerb => "Analysing";

    public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods)
        => new DrawableManiaMapAnalyserRuleset(this, beatmap, mods);

    public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap)
        => new ManiaMapAnalyserBeatmapConverter(beatmap, this);

    public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap)
        => new ManiaMapAnalyserDifficultyCalculator(RulesetInfo, beatmap);

    public override IEnumerable<Mod> GetModsFor(ModType type) => Array.Empty<Mod>();

    public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0) => Array.Empty<KeyBinding>();

    public override IRulesetConfigManager CreateConfig(SettingsStore? settings) => new ManiaMapAnalyserRulesetConfigManager(settings, RulesetInfo);

    public override RulesetSettingsSubsection CreateSettings() => new ManiaMapAnalyserMainSection(this);

    public override Drawable CreateIcon() => new RulesetIcon();

    public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

    private partial class RulesetIcon : CompositeDrawable
    {
        private const int max_injection_attempts = 50;

        public RulesetIcon()
        {
            AutoSizeAxes = Axes.Both;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            beginInjectWhenAttached();
        }

        private void beginInjectWhenAttached(int attempt = 0)
        {
            OsuGame? game = findParentGame();

            if (game != null)
            {
                InjectorBootstrapper.BeginInject(game, Scheduler);
                return;
            }

            if (attempt >= max_injection_attempts)
                return;

            Scheduler.AddDelayed(() => beginInjectWhenAttached(attempt + 1), 100);
        }

        private OsuGame? findParentGame()
        {
            for (Drawable? current = this; current != null; current = current.Parent)
            {
                if (current is OsuGame game)
                    return game;
            }

            return null;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Circle
                {
                    Size = new Vector2(20),
                    Colour = Color4.Black.Opacity(0.25f),
                    BorderColour = Color4.White,
                    BorderThickness = 2,
                },
                new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = FontAwesome.Solid.ChartLine,
                    Size = new Vector2(10),
                },
            };
        }
    }

    private sealed class ManiaMapAnalyserBeatmapConverter : BeatmapConverter<ManiaMapAnalyserHitObject>
    {
        public ManiaMapAnalyserBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
            : base(beatmap, ruleset)
        {
        }

        public override bool CanConvert() => true;

        protected override IEnumerable<ManiaMapAnalyserHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            yield return new ManiaMapAnalyserHitObject
            {
                StartTime = original.StartTime,
                Samples = original.Samples,
            };
        }
    }

    private sealed class ManiaMapAnalyserDifficultyCalculator : DifficultyCalculator
    {
        public ManiaMapAnalyserDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
            => new DifficultyAttributes(mods, 0);

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, double clockRate)
            => Array.Empty<DifficultyHitObject>();

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods, double clockRate)
            => Array.Empty<Skill>();
    }

    private sealed class ManiaMapAnalyserHitObject : HitObject
    {
    }

    private sealed partial class DrawableManiaMapAnalyserRuleset : DrawableRuleset<ManiaMapAnalyserHitObject>
    {
        public DrawableManiaMapAnalyserRuleset(ManiaMapAnalyserRuleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods)
            : base(ruleset, beatmap, mods)
        {
        }

        protected override Playfield CreatePlayfield() => new ManiaMapAnalyserPlayfield();

        public override DrawableHitObject<ManiaMapAnalyserHitObject> CreateDrawableRepresentation(ManiaMapAnalyserHitObject hitObject)
            => new DrawableManiaMapAnalyserHitObject(hitObject);

        protected override PassThroughInputManager CreateInputManager() => new ManiaMapAnalyserInputManager(Ruleset!.RulesetInfo);

        protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new ManiaMapAnalyserReplayInputHandler(replay);
    }

    private sealed partial class ManiaMapAnalyserPlayfield : Playfield
    {
    }

    private sealed partial class DrawableManiaMapAnalyserHitObject : DrawableHitObject<ManiaMapAnalyserHitObject>
    {
        public DrawableManiaMapAnalyserHitObject(ManiaMapAnalyserHitObject hitObject)
            : base(hitObject)
        {
            Alpha = 0;
            Size = Vector2.Zero;
            AlwaysPresent = false;
        }

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (timeOffset >= 0)
                ApplyResult(HitResult.Perfect);
        }
    }

    private sealed class ManiaMapAnalyserReplayInputHandler : FramedReplayInputHandler<ManiaMapAnalyserReplayFrame>
    {
        public ManiaMapAnalyserReplayInputHandler(Replay replay)
            : base(replay)
        {
        }

        protected override bool IsImportant(ManiaMapAnalyserReplayFrame frame) => false;

        protected override void CollectReplayInputs(List<IInput> inputs)
        {
        }
    }

    private sealed class ManiaMapAnalyserReplayFrame : ReplayFrame
    {
        public override bool IsEquivalentTo(ReplayFrame other) => other.Time == Time;
    }

    private sealed partial class ManiaMapAnalyserInputManager : RulesetInputManager<ManiaMapAnalyserAction>
    {
        public ManiaMapAnalyserInputManager(RulesetInfo ruleset)
            : base(ruleset, 0, SimultaneousBindingMode.None)
        {
        }
    }

    private enum ManiaMapAnalyserAction
    {
    }
}
