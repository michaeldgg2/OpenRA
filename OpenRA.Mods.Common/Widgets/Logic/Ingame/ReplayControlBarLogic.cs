#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Mods.Common.Lint;
using OpenRA.Network;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	[ChromeLogicArgsHotkeys("ReplaySpeedSlowKey", "ReplaySpeedRegularKey", "ReplaySpeedFastKey", "ReplaySpeedMaxKey")]
	public class ReplayControlBarLogic : ChromeLogic
	{
		[FluentReference]
		const string PlaybackMaxSpeedLabel = "label-slider-replay-maximum";

		enum PlaybackSpeed { VerySlow, Slow, Regular, Fast, VeryFast, UltraFast, Maximum }

		readonly Dictionary<PlaybackSpeed, float> multipliers = new()
		{
			{ PlaybackSpeed.VerySlow, 4f },
			{ PlaybackSpeed.Slow, 2f },
			{ PlaybackSpeed.Regular, 1f },
			{ PlaybackSpeed.Fast, 0.5f },
			{ PlaybackSpeed.VeryFast, 0.25f },
			{ PlaybackSpeed.UltraFast, 0.125f },
			{ PlaybackSpeed.Maximum, 0.001f },
		};

		[ObjectCreator.UseCtor]
		public ReplayControlBarLogic(Widget widget, ModData modData, World world, OrderManager orderManager, Dictionary<string, MiniYaml> logicArgs)
		{
			if (!world.IsReplay)
				return;

			var container = widget.Get("REPLAY_PLAYER");
			var connection = (ReplayConnection)orderManager.Connection;
			var replayNetTicks = connection.TickCount;

			var background = widget.Parent.GetOrNull("OBSERVER_CONTROL_BG");
			if (background != null)
				background.Bounds.Height += container.Bounds.Height;

			container.Visible = true;
			var speed = PlaybackSpeed.Regular;
			var originalTimestep = world.Timestep;

			// In the event the replay goes out of sync, it becomes no longer usable. For polish we permanently pause the world.
			bool IsWidgetDisabled() => orderManager.IsOutOfSync || orderManager.NetFrameNumber >= replayNetTicks;

			var pauseButton = widget.Get<ButtonWidget>("BUTTON_PAUSE");
			pauseButton.IsVisible = () => world.ReplayTimestep != 0 && !IsWidgetDisabled();
			pauseButton.OnClick = () => world.ReplayTimestep = 0;

			var playButton = widget.Get<ButtonWidget>("BUTTON_PLAY");
			playButton.IsVisible = () => world.ReplayTimestep == 0 || IsWidgetDisabled();
			playButton.OnClick = () => world.ReplayTimestep = (int)Math.Ceiling(originalTimestep * multipliers[speed]);
			playButton.IsDisabled = IsWidgetDisabled;

			var orderedSpeeds = Enum.GetValues<PlaybackSpeed>();

			void SetSpeed(PlaybackSpeed s)
			{
				speed = s;
				if (world.ReplayTimestep != 0)
					world.ReplayTimestep = (int)Math.Ceiling(originalTimestep * multipliers[speed]);
			}

			var speedSlider = widget.Get<SliderWidget>("SPEED_SLIDER");
			var speedLabel = widget.Get<LabelWidget>("SPEED_LABEL");
			speedSlider.MaximumValue = orderedSpeeds.Length - 1;
			speedSlider.Ticks = orderedSpeeds.Length;
			speedSlider.Value = Array.IndexOf(orderedSpeeds, speed);
			speedSlider.GetValue = () => Array.IndexOf(orderedSpeeds, speed);
			speedSlider.IsDisabled = IsWidgetDisabled;
			speedSlider.OnChange += x =>
			{
				var idx = (int)Math.Round(x).Clamp(0, orderedSpeeds.Length - 1);
				SetSpeed(orderedSpeeds[idx]);
			};

			speedLabel.GetText = () =>
			{
				return speed switch
				{
					PlaybackSpeed.VerySlow => "25%",
					PlaybackSpeed.Slow => "50%",
					PlaybackSpeed.Regular => "100%",
					PlaybackSpeed.Fast => "200%",
					PlaybackSpeed.VeryFast => "400%",
					PlaybackSpeed.UltraFast => "800%",
					PlaybackSpeed.Maximum => FluentProvider.GetMessage(PlaybackMaxSpeedLabel),
					_ => ""
				};
			};

			var slowKey = new HotkeyReference();
			var regularKey = new HotkeyReference();
			var fastKey = new HotkeyReference();
			var maxKey = new HotkeyReference();

			if (logicArgs != null)
			{
				if (logicArgs.TryGetValue("ReplaySpeedSlowKey", out var sk))
					slowKey = modData.Hotkeys[sk.Value];
				if (logicArgs.TryGetValue("ReplaySpeedRegularKey", out var rk))
					regularKey = modData.Hotkeys[rk.Value];
				if (logicArgs.TryGetValue("ReplaySpeedFastKey", out var fk))
					fastKey = modData.Hotkeys[fk.Value];
				if (logicArgs.TryGetValue("ReplaySpeedMaxKey", out var mk))
					maxKey = modData.Hotkeys[mk.Value];
			}

			var keyhandler = widget.Get<LogicKeyListenerWidget>("REPLAY_KEYHANDLER");
			keyhandler.AddHandler(e =>
			{
				if (e.Event != KeyInputEvent.Down || IsWidgetDisabled())
					return false;

				if (slowKey.IsActivatedBy(e))
				{
					SetSpeed(PlaybackSpeed.Slow);
					return true;
				}

				if (regularKey.IsActivatedBy(e))
				{
					SetSpeed(PlaybackSpeed.Regular);
					return true;
				}

				if (fastKey.IsActivatedBy(e))
				{
					SetSpeed(PlaybackSpeed.Fast);
					return true;
				}

				if (maxKey.IsActivatedBy(e))
				{
					SetSpeed(PlaybackSpeed.Maximum);
					return true;
				}

				return false;
			});
		}
	}
}
