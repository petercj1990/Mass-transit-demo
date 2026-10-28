using System.Collections.Generic;
using System.Text;
using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    public sealed partial class Hud
    {
        private const float CardW = 268f;
        private const float CardH = 128f;

        private static string RoleLetter(Role r)
        {
            switch (r)
            {
                case Role.Tank: return "T";
                case Role.Healer: return "H";
                case Role.Melee: return "M";
                case Role.Siege: return "S";
                case Role.Warwright: return "W";
            }
            return "?";
        }

        private static string TaskText(World w, Squad sq)
        {
            if (sq.Wiped) return "<color=#ff6050>WIPED: recover the kin-cores</color>";
            if (sq.Garrisoned) return "Garrisoned at base";
            if (sq.InCombat) return "<color=#ff9a6a>In combat</color>";
            switch (sq.Task)
            {
                case SquadTask.Move: return "Moving";
                case SquadTask.Attack: return "Attacking";
                case SquadTask.Gather: return sq.Arrived ? "Gathering" : "Heading to gather";
                case SquadTask.Return: return "Hauling home";
                case SquadTask.Garrison: return "Heading to garrison";
                case SquadTask.Recover: return "Recovering kin-cores";
                case SquadTask.FallBack: return "Falling back";
            }
            return sq.Stance == CommandType.Rest ? "Resting" : "Holding";
        }

        // =====================================================================================
        // Party frames
        // =====================================================================================

        private const float FrameW = 262f;
        private const float FrameH = 50f;

        /// <summary>
        /// WoW-style party frames: one per Vhorn with health (the dark red end is wounds that only rest
        /// at base heals), mana and stamina, plus a header with the strategy, task and haul.
        /// </summary>
        private void DrawPartyFrames(World w)
        {
            var sq = _g.Commands.Squad;
            if (sq == null) return;
            var sel = _g.Selection;
            float x = 8, y = 34;
            var head = Panel(new Rect(x, y, FrameW, 44));
            Color c = Palette.Squad(sq.ColorIndex);
            Swatch(new Rect(head.x + 8, head.y + 7, 10, 10), c);
            GUI.Label(new Rect(head.x + 22, head.y + 2, 130, 20), $"<b>{sq.Name}</b>", _label);
            GUI.Label(new Rect(head.x + 120, head.y + 3, 134, 20), $"<color=#{Hex(Palette.KinMark)}>{CommandInfo.Get(sq.Stance).Name}</color>", _smallRight);
            float load = sq.CarryLoad, capy = Mathf.Max(1f, sq.CarryCapacity);
            string sub = TaskText(w, sq) + $"   haul {load:0}/{capy:0}";
            if (sq.CoresCarried > 0) sub += $"   <color=#{Hex(Palette.KinMark)}>{sq.CoresCarried} kin-core(s)</color>";
            GUI.Label(new Rect(head.x + 8, head.y + 21, FrameW - 16, 18), sub, _tiny);
            y += 48;

            foreach (var m in sq.Members)
            {
                var r = Panel(new Rect(x, y, FrameW, FrameH));
                if (sel.InspectUnitId == m.Id) Swatch(new Rect(r.x, r.y, 2, r.height), c);
                if (GUI.Button(r, GUIContent.none, _hidden)) sel.InspectUnitId = m.Id;

                _tinyBold.normal.textColor = c;
                GUI.Label(new Rect(r.x + 7, r.y + 2, 14, 16), RoleLetter(m.Role), _tinyBold);
                GUI.Label(new Rect(r.x + 20, r.y + 1, 120, 18), m.Name, _small);
                GUI.Label(new Rect(r.x + 112, r.y + 2, FrameW - 120, 16), StateTags(m), _tinyRightFrame);

                float bx = r.x + 8, bw = FrameW - 16;
                // Health: green = current, dark red = wounds (lost until resting at base), dark = healable.
                var hpR = new Rect(bx, r.y + 19, bw, 11);
                Swatch(hpR, new Color(0f, 0f, 0f, 0.6f));
                if (m.Life != LifeState.Dead)
                {
                    float cap = Mathf.Clamp01(1f - m.Wound);
                    Swatch(new Rect(hpR.x + hpR.width * cap, hpR.y, hpR.width * (1f - cap), hpR.height), new Color(0.42f, 0.08f, 0.07f, 0.95f));
                    float hp = m.Life == LifeState.Active ? m.HpFraction : 0f;
                    Color hc = m.TitanTime > 0f ? new Color(1f, 0.68f, 0.25f) : m.Injured ? new Color(0.95f, 0.65f, 0.2f) : new Color(0.32f, 0.85f, 0.38f);
                    Swatch(new Rect(hpR.x, hpR.y, hpR.width * hp, hpR.height), hc);
                    GUI.Label(new Rect(hpR.x, hpR.y - 3, hpR.width, 16), m.Life == LifeState.Active ? $"{m.Hp:0} / {m.HealCap:0}" : "", _tinyCenterWhite);
                }
                // Mana and stamina.
                Bar(new Rect(bx, r.y + 33, bw, 5), m.Life == LifeState.Active ? m.ManaFraction : 0f, new Color(0.3f, 0.55f, 1f), new Color(0f, 0f, 0f, 0.55f));
                Bar(new Rect(bx, r.y + 41, bw, 5), m.Life == LifeState.Active ? m.StaminaFraction : 0f,
                    m.Exhausted ? new Color(0.9f, 0.4f, 0.2f) : new Color(0.95f, 0.8f, 0.3f), new Color(0f, 0f, 0f, 0.55f));
                y += FrameH + 4;
            }
        }

        private static string StateTags(Unit m)
        {
            switch (m.Life)
            {
                case LifeState.Dead: return m.Core == CoreState.AtCradle ? "<color=#ffb040>regrowing</color>" : "<color=#ff5040>DEAD</color>";
                case LifeState.Downed: return "<color=#ff7060>DOWN</color>";
            }
            var sb = new StringBuilder();
            if (m.TitanTime > 0f) sb.Append("<color=#ffb040>TITAN</color> ");
            if (m.Injured) sb.Append("<color=#ffb040>injured</color> ");
            if (m.Exhausted) sb.Append("<color=#ff8a50>exhausted</color> ");
            if (m.Wound >= 0.35f) sb.Append("<color=#d05050>wounded</color> ");
            if (m.Action == UnitAction.Resting) sb.Append("resting ");
            if (m.Garrisoned) sb.Append("home ");
            return sb.ToString();
        }

        // =====================================================================================
        // Strategy hotbar
        // =====================================================================================

        private void DrawHotbar(World w)
        {
            var sq = _g.Commands.Squad;
            if (sq == null) return;
            const float bw = 72f, bh = 42f, gap = 4f, ow = 84f;
            int n = CommandInput.CommandForKey.Length;
            float width = n * (bw + gap) + 3 * (ow + gap) + 12;
            var r = Panel(new Rect(W / 2 - width / 2, H - bh - 30, width, bh + 22));
            GUI.Label(new Rect(r.x + 8, r.y + 2, width - 16, 16),
                "<size=10>WASD / left stick: move the kin-band   drag / right stick: camera   right-click: attack, gather, recover, go home   M: map</size>", _tiny);

            CommandInfo hovered = null;
            float by = r.y + 18;
            for (int i = 0; i < n; i++)
            {
                var cmd = CommandInput.CommandForKey[i];
                var info = CommandInfo.Get(cmd);
                var br = new Rect(r.x + 6 + i * (bw + gap), by, bw, bh);
                bool on = sq.Stance == cmd;
                if (GUI.Button(br, $"<size=10>{info.Hotkey}</size>\n{ShortName(cmd)}", on ? _buttonOn : _button)) _g.Commands.IssueCommand(cmd);
                if (br.Contains(Event.current.mousePosition)) hovered = info;
            }
            float ox = r.x + 6 + n * (bw + gap) + 4;
            if (GUI.Button(new Rect(ox, by, ow, bh), "<size=10>H</size>\nGo home", _button)) _g.Commands.IssueSimple(OrderType.ReturnToBase);
            if (GUI.Button(new Rect(ox + ow + gap, by, ow, bh), "<size=10>J</size>\nGarrison", _button)) _g.Commands.IssueSimple(OrderType.Garrison);
            if (GUI.Button(new Rect(ox + 2 * (ow + gap), by, ow, bh), "<size=10>K</size>\nStop", _button)) _g.Commands.IssueSimple(OrderType.Stop);

            if (hovered != null)
            {
                string tip = $"<b>{hovered.Name}</b> [{hovered.Hotkey}]\n{hovered.Does}\n<color=#c8b48c>Warwright:</color> {hovered.Warwright}\n<color=#c8b48c>Siegewright:</color> {hovered.Siegewright}\n<color=#8fdc8f>Strong vs:</color> {hovered.StrongAgainst}\n<color=#ff8f80>Risk:</color> {hovered.Risk}";
                float th = _tooltip.CalcHeight(new GUIContent(tip), 380);
                GUI.Label(new Rect(r.x, r.y - th - 6, 380, th), tip, _tooltip);
            }
        }

        private static string ShortName(CommandType c)
        {
            switch (c)
            {
                case CommandType.DefensiveFormation: return "Defend";
                case CommandType.HoldPosition: return "Hold";
                case CommandType.FocusFire: return "Focus";
                case CommandType.SpreadOut: return "Spread";
                case CommandType.FallBack: return "Fall back";
            }
            return CommandInfo.Get(c).Name;
        }

        // =====================================================================================
        // Objectives
        // =====================================================================================

        /// <summary>The right column starts under the minimap (top right, as in WoW).</summary>
        private float RightColumnTop => 34 + MiniSize + 18;

        private void DrawObjectives(World w)
        {
            float x = W - 336, y = RightColumnTop;
            if (!_showObjectives)
            {
                var small = Panel(new Rect(x, y, 328, 24));
                if (GUI.Button(small, GUIContent.none, _hidden)) _showObjectives = true;
                GUI.Label(new Rect(small.x + 8, small.y + 3, 300, 20), "<b>Objectives</b>  <size=10>(click to show)</size>", _small);
                return;
            }
            var r = Panel(new Rect(x, y, 328, 128));
            if (GUI.Button(new Rect(r.x, r.y, r.width, 20), GUIContent.none, _hidden)) _showObjectives = false;
            GUI.Label(new Rect(r.x + 8, r.y + 3, 300, 18), "<b>Objective: clear the Craters</b>  <size=10>(click to hide)</size>", _small);
            var b = w.Base;
            bool gear = false;
            foreach (var sq in w.Squads) gear |= sq.WeaponTier > 1 || sq.ArmorTier > 1;
            var steps = new List<(bool done, string text)>
            {
                (w.Reports.Count > 0, "Fight a camp; Doctrine grows with every fight"),
                (w.Stock[ResourceKind.Ferrite] >= 20f || w.Stock[ResourceKind.Alloy] > 0f, "Gather ferrite (crater beds) and haul it home"),
                (gear, "Garrison at home: heal wounds, smelt alloy, craft gear"),
                (b != null && b.WarHallLevel >= 2, "Upgrade the War Hall: drills make the kin-band stronger"),
                (b != null && b.SigilCrafted, "Craft the Breach Sigil to open Breach Hold"),
                (w.Victory, "Defeat Rustmaw (3 phases)"),
            };
            float sy = r.y + 22;
            foreach (var s in steps)
            {
                string mark = s.done ? $"<color=#{Hex(Palette.Heal)}>[x]</color>" : "<color=#888888>[ ]</color>";
                GUI.Label(new Rect(r.x + 10, sy, 312, 17), $"{mark} {(s.done ? "<color=#9a9a9a>" + s.text + "</color>" : s.text)}", _tiny);
                sy += 17;
            }
        }

        // =====================================================================================
        // Inspector
        // =====================================================================================

        private void DrawInspect(World w)
        {
            var u = w.FindUnitAny(_g.Selection.InspectUnitId);
            if (u == null || (u.Removed && !u.IsPlayer)) return;
            float top = RightColumnTop + (_showObjectives ? 134 : 30);
            float height = Mathf.Min(H - top - 76, u.IsPlayer ? 560 : 300);
            if (height < 120) return;
            var r = Panel(new Rect(W - 336, top, 328, height));
            if (u.IsPlayer) InspectPlayer(w, u, r);
            else InspectEnemy(w, u, r);
        }

        private void InspectPlayer(World w, Unit u, Rect r)
        {
            float x = r.x + 10, y = r.y + 6, cw = r.width - 20;
            var sq = u.Squad;
            GUI.Label(new Rect(x, y, cw, 20), $"<b>{u.Name}</b>  <color=#bbbbbb>{u.Def.Name} of {sq?.Name}</color>", _label);
            y += 20;
            string state = u.Life == LifeState.Dead ? "<color=#ff5040>Dead</color> (kin-core " + u.Core + ")" :
                           u.Life == LifeState.Downed ? "<color=#ff7060>Downed</color>" :
                           u.Injured ? $"<color=#ffb040>Injured</color> (rest {u.RestProgress:0}/{Balance.RestSecondsToClearInjury:0}s)" : "Healthy";
            string gear = $"{(u.WeaponTier > 1 ? "Ferrite" : "Scrap")} arms, {(u.ArmorTier > 1 ? "ferrite" : "scrap")} plate";
            GUI.Label(new Rect(x, y, cw, 18), $"{u.Hp:0}/{u.MaxHp:0} hp   armour {u.Armor:0}   {state}", _small);
            y += 16;
            string wound = u.Wound > 0.005f ? $"   <color=#d05050>wounds {u.Wound:P0}</color> (heal at home)" : "";
            GUI.Label(new Rect(x, y, cw, 16), $"<color=#7fa8ff>mana {u.Mana:0}/{u.MaxMana:0}</color>   <color=#f0cc55>stamina {u.Stamina:0}/{u.MaxStamina:0}</color>{wound}", _tiny);
            y += 14;
            if (u.TitanTime > 0f)
            {
                GUI.Label(new Rect(x, y, cw, 16), $"<color=#{Hex(Palette.KinMark)}>Titan Form</color>: +35% health, +{Balance.TitanArmor:0} armour, x{Balance.TitanThreatMul:0.#} threat, slower", _tiny);
                y += 14;
            }
            GUI.Label(new Rect(x, y, cw, 18), $"{gear}   fights {u.Fights}   kills {u.Kills}", _tiny);
            y += 18;

            // Abilities
            GUI.Label(new Rect(x, y, cw, 18), "<b>Abilities</b> <size=10>(the unit decides when to use them)</size>", _small);
            y += 17;
            if (u.Abilities != null)
            {
                for (int i = 0; i < u.Abilities.Length; i++)
                {
                    var s = u.Abilities[i];
                    string txt = !s.Unlocked ? $"<color=#777777>{s.Def.Name}: unlocks at Doctrine tier {PlayerUnits.SlotUnlockTier(i)} (any command)</color>"
                        : s.Def.Id == AbilityId.TitanForm && u.TitanTime > 0f ? $"{s.Def.Name} <color=#ffb040>ACTIVE {u.TitanTime:0.0}s</color>"
                        : s.Cooldown > 0f ? $"{s.Def.Name} <color=#999999>{s.Cooldown:0.0}s</color>" : $"{s.Def.Name} <color=#8fdc8f>ready</color>";
                    GUI.Label(new Rect(x + 6, y, cw - 6, 16), txt, _tiny);
                    y += 14;
                }
            }
            y += 4;

            // Doctrine
            GUI.Label(new Rect(x, y, cw, 18), $"<b>Doctrine</b> <size=10>(unbanked {u.Doctrine.UnbankedTotal:0} XP: bank it at base)</size>", _small);
            y += 17;
            foreach (var cmd in CommandInfo.Playable)
            {
                int tier = u.Doctrine.Tier(cmd);
                bool current = sq != null && sq.Stance == cmd;
                GUI.Label(new Rect(x + 6, y - 1, 120, 15), current ? $"<b><color=#{Hex(Palette.KinMark)}>{CommandInfo.Get(cmd).Name}</color></b>" : CommandInfo.Get(cmd).Name, _tiny);
                for (int t = 0; t < 5; t++) Swatch(new Rect(x + 130 + t * 9, y + 3, 7, 7), t < tier ? Palette.KinMark : new Color(1f, 1f, 1f, 0.12f));
                Bar(new Rect(x + 180, y + 4, 96, 5), u.Doctrine.Progress(cmd), new Color(1f, 0.75f, 0.3f, 0.8f), new Color(0f, 0f, 0f, 0.5f));
                GUI.Label(new Rect(x + 280, y - 1, 40, 15), $"{u.Doctrine.Total(cmd):0}", _tiny);
                y += 13;
            }
            y += 4;

            // Instinct
            var cmdNow = sq != null ? sq.Stance : CommandType.DefensiveFormation;
            int tierNow = u.Doctrine.Tier(cmdNow);
            var fam = sq?.Fight?.DominantFamily ?? EnemyFamily.None;
            if (fam == EnemyFamily.None) fam = MostTrainedFamily(u, cmdNow);
            GUI.Label(new Rect(x, y, cw, 18), $"<b>Instinct</b> <size=10>({CommandInfo.Get(cmdNow).Name} vs {Instinct.FamilyName(fam)}, bounded by tier {tierNow})</size>", _small);
            y += 17;
            foreach (var p in u.Instinct.Params)
            {
                u.Instinct.Bounds(p, tierNow, out float lo, out float hi);
                float v = u.Instinct.Learned(cmdNow, fam, p.Param, tierNow);
                GUI.Label(new Rect(x + 6, y - 1, 110, 15), p.Param.ToString(), _tiny);
                var track = new Rect(x + 118, y + 4, 140, 5);
                float span = Mathf.Max(0.0001f, p.Max - p.Min);
                Swatch(track, new Color(0f, 0f, 0f, 0.5f));
                if (p.Param != InstinctParam.Reaction)
                {
                    Swatch(new Rect(track.x + track.width * (lo - p.Min) / span, track.y, track.width * (hi - lo) / span, track.height), new Color(1f, 0.7f, 0.25f, 0.35f));
                    Swatch(new Rect(track.x + track.width * (p.Default - p.Min) / span - 0.5f, track.y - 2, 1, track.height + 4), new Color(1f, 1f, 1f, 0.35f));
                    Swatch(new Rect(track.x + track.width * (v - p.Min) / span - 1.5f, track.y - 2, 3, track.height + 4), Palette.KinMark);
                }
                else
                {
                    Swatch(new Rect(track.x, track.y, track.width * Mathf.Clamp01((v - 0.35f) / 1.35f), track.height), Palette.KinMark);
                }
                GUI.Label(new Rect(x + 264, y - 1, 56, 15), v.ToString(v >= 10f ? "0" : "0.00"), _tiny);
                y += 13;
            }
            float bx = x;
            if (GUI.Button(new Rect(bx, y + 3, 150, 20), u.Instinct.Locked ? "Unlock Instinct" : "Lock Instinct", u.Instinct.Locked ? _buttonOn : _button))
                w.Issue(new PlayerOrder { Type = OrderType.LockInstinct, TargetId = u.Id, Arg = u.Instinct.Locked ? 0 : 1 });
            if (GUI.Button(new Rect(bx + 156, y + 3, 150, 20), $"Reset {CommandInfo.Get(cmdNow).Name}", _button))
                w.Issue(new PlayerOrder { Type = OrderType.ResetInstinct, TargetId = u.Id, Command = cmdNow });
            y += 28;

            // Utility AI debug view
            GUI.Label(new Rect(x, y, cw, 18), "<b>AI thinking</b> <size=10>(top utility scores right now)</size>", _small);
            y += 16;
            for (int i = 0; i < u.DebugCount && y < r.yMax - 14; i++)
            {
                bool chosen = i == 0;
                GUI.Label(new Rect(x + 6, y - 1, cw - 50, 15), chosen ? $"<b>{u.DebugLabel[i]}</b>" : u.DebugLabel[i], _tiny);
                GUI.Label(new Rect(x + cw - 40, y - 1, 40, 15), u.DebugScore[i].ToString("0"), _tiny);
                y += 13;
            }
        }

        private static EnemyFamily MostTrainedFamily(Unit u, CommandType c)
        {
            EnemyFamily best = EnemyFamily.Cratermaw;
            int bestN = -1;
            foreach (var kv in u.Instinct.Sets)
            {
                if (kv.Key / 16 != (int)c) continue;
                if (kv.Value.Samples > bestN)
                {
                    bestN = kv.Value.Samples;
                    best = (EnemyFamily)(kv.Key % 16);
                }
            }
            return best;
        }

        private void InspectEnemy(World w, Unit u, Rect r)
        {
            float x = r.x + 10, y = r.y + 6, cw = r.width - 20;
            GUI.Label(new Rect(x, y, cw, 20), $"<b>{u.Def.Name}</b>", _title);
            y += 22;
            GUI.Label(new Rect(x, y, cw, 32), u.Def.Blurb, _wrapTiny);
            y += 30;
            GUI.Label(new Rect(x, y, cw, 18), $"{u.Hp:0}/{u.MaxHp:0} hp   armour {u.Armor:0}   {(u.Life == LifeState.Active ? "" : "dead")}", _small);
            y += 20;
            GUI.Label(new Rect(x, y, cw, 18), "<b>Bestiary</b> <size=10>(learned by hitting it with each damage type)</size>", _small);
            y += 17;
            for (int i = 0; i < 6; i++)
            {
                var t = (DamageType)i;
                string val;
                if (!w.Bestiary.Knows(u.Def.Family, t)) val = "<color=#777777>?</color>";
                else
                {
                    float m = u.Def.ResistTo(t);
                    val = m > 1.05f ? $"<color=#8fdc8f>weak x{m:0.0#}</color>" : m < 0.95f ? $"<color=#ff8f80>resists x{m:0.0#}</color>" : "normal";
                }
                Swatch(new Rect(x + 6, y + 4, 7, 7), Palette.Damage(t));
                GUI.Label(new Rect(x + 18, y - 1, 90, 15), t.ToString(), _tiny);
                GUI.Label(new Rect(x + 110, y - 1, 150, 15), val, _tiny);
                y += 14;
            }
            y += 6;
            if (u.Threat != null && u.Threat.Count > 0)
            {
                GUI.Label(new Rect(x, y, cw, 18), "<b>Threat</b> <size=10>(it attacks the top of this list)</size>", _small);
                y += 17;
                var rows = new List<(string, float)>();
                for (int i = 0; i < u.Threat.Count; i++)
                {
                    var e = w.FindEntity(u.Threat.IdAt(i));
                    string n = e is Unit tu ? $"{tu.Name} ({tu.Def.Name})" : (e is Structure s ? s.Kind.ToString() : "?");
                    rows.Add((n, u.Threat.ValueAt(i)));
                }
                rows.Sort((a, b) => b.Item2.CompareTo(a.Item2));
                for (int i = 0; i < Mathf.Min(5, rows.Count) && y < r.yMax - 14; i++)
                {
                    GUI.Label(new Rect(x + 6, y - 1, cw - 60, 15), (i == 0 ? "> " : "   ") + rows[i].Item1, _tiny);
                    GUI.Label(new Rect(x + cw - 50, y - 1, 50, 15), rows[i].Item2.ToString("0"), _tiny);
                    y += 13;
                }
            }
        }

        // =====================================================================================
        // Battle reports
        // =====================================================================================

        private void DrawReportToast(World w)
        {
            if (w.Reports.Count > _reportSeen)
            {
                _reportSeen = w.Reports.Count;
                _reportShownAt = Time.unscaledTime;
            }
            if (w.Reports.Count == 0 || Time.unscaledTime - _reportShownAt > 16f || _showLog) return;
            var rep = w.Reports[w.Reports.Count - 1];
            var lines = new List<ReportLine>();
            foreach (var l in rep.Lines)
            {
                if (l.Kind != 0) lines.Add(l);
            }
            int shown = Mathf.Min(8, lines.Count);
            float h = 46 + shown * 14;
            var r = Panel(new Rect(W - 336, H - h - 8, 328, h));
            GUI.Label(new Rect(r.x + 10, r.y + 4, 308, 20), $"<b>Battle report: {rep.SquadName}</b>  {ResultColored(rep.Result)}", _small);
            GUI.Label(new Rect(r.x + 10, r.y + 20, 308, 18), $"{rep.Duration:0}s, {rep.Kills} kills vs {rep.Enemies}", _tiny);
            float y = r.y + 38;
            for (int i = 0; i < shown; i++)
            {
                var l = lines[i];
                string c = l.Kind == 1 ? Hex(Palette.KinMark) : l.Kind == 2 ? "9fd0ff" : "ff8f80";
                GUI.Label(new Rect(r.x + 10, y - 1, 308, 15), $"<color=#{c}>{l.Who}</color> {l.Text}", _tiny);
                y += 14;
            }
        }

        private static string ResultColored(string result)
        {
            if (result == "Victory") return "<color=#8fdc8f>Victory</color>";
            if (result == "Squad wiped") return "<color=#ff5040>Squad wiped</color>";
            return $"<color=#ffcc66>{result}</color>";
        }

        private void DrawLog(World w)
        {
            var r = Panel(new Rect(W / 2 - 300, 90, 600, H - 260));
            GUI.Label(new Rect(r.x + 10, r.y + 6, 400, 20), "<b>Battle reports</b>  <size=10>(F3 to close)</size>", _title);
            var view = new Rect(r.x + 6, r.y + 30, r.width - 12, r.height - 36);
            float contentH = 0f;
            for (int i = w.Reports.Count - 1; i >= 0; i--) contentH += 40 + w.Reports[i].Lines.Count * 14;
            _logScroll = GUI.BeginScrollView(view, _logScroll, new Rect(0, 0, view.width - 18, Mathf.Max(view.height, contentH)));
            float y = 0;
            for (int i = w.Reports.Count - 1; i >= 0; i--)
            {
                var rep = w.Reports[i];
                GUI.Label(new Rect(4, y, 560, 18), $"<b>[{Clock(rep.Time)}] {rep.SquadName}</b>  {ResultColored(rep.Result)}  {rep.Duration:0}s vs {rep.Enemies}", _small);
                y += 18;
                foreach (var l in rep.Lines)
                {
                    string c = l.Kind == 0 ? "bbbbbb" : l.Kind == 1 ? Hex(Palette.KinMark) : l.Kind == 2 ? "9fd0ff" : "ff8f80";
                    GUI.Label(new Rect(16, y, 540, 15), $"<color=#{c}>{l.Who}</color> {l.Text}", _tiny);
                    y += 14;
                }
                y += 22;
            }
            GUI.EndScrollView();
        }

        // =====================================================================================
        // Base panel
        // =====================================================================================

        private string CostText(World w, Cost c)
        {
            if (c == null) return "";
            var sb = new StringBuilder();
            foreach (var it in c.Items)
            {
                if (sb.Length > 0) sb.Append(", ");
                bool ok = w.Stock[it.Kind] + 0.001f >= it.Amount;
                sb.Append(ok ? "" : "<color=#ff7060>").Append(it.Amount).Append(' ').Append(Stockpile.Name(it.Kind)).Append(ok ? "" : "</color>");
            }
            return sb.ToString();
        }

        private void DrawBasePanel(World w)
        {
            var b = w.Base;
            if (b == null) return;
            var r = Panel(new Rect(W / 2 - 300, 110, 600, 420));
            float x = r.x + 12, y = r.y + 8, cw = r.width - 24;
            GUI.Label(new Rect(x, y, 400, 22), "<b>Clan base</b>  <size=10>(F2 or Esc to close)</size>", _title);
            if (GUI.Button(new Rect(r.xMax - 30, r.y + 6, 22, 20), "x", _button)) _showBase = false;
            y += 26;
            int garrisons = b.GarrisonCount(w);
            GUI.Label(new Rect(x, y, cw, 18), garrisons > 0
                ? "<color=#8fdc8f>The kin-band is home</color>: wounds are closing, mana and stamina refill, and the Smelter and Salvage Bench are running."
                : "<color=#ffb040>The kin-band is out.</color> Wounds only heal, and the workshops only run, while it is garrisoned at home [J].", _wrapTiny);
            y += 30;

            // War Hall
            int lvl = b.WarHallLevel;
            GUI.Label(new Rect(x, y, cw, 18), $"<b>War Hall</b>  level {lvl}: drills give +{Balance.HallHealthPerLevel * (lvl - 1):P0} health, +{Balance.HallResourcePerLevel * (lvl - 1):P0} mana and stamina", _small);
            y += 18;
            if (b.WarHallLevel < Balance.MaxWarHallLevel)
            {
                var cost = PlayerBase.WarHallCost[b.WarHallLevel];
                bool can = w.Stock.Has(cost);
                if (GUI.Button(new Rect(x, y, 200, 22), "Upgrade War Hall", can ? _button : _buttonOff)) w.Issue(new PlayerOrder { Type = OrderType.UpgradeWarHall });
                GUI.Label(new Rect(x + 210, y + 3, cw - 210, 18), CostText(w, cost), _tiny);
            }
            else GUI.Label(new Rect(x, y, cw, 18), "Fully upgraded for this milestone.", _tiny);
            y += 30;

            // Kin Cradle
            GUI.Label(new Rect(x, y, cw, 18), $"<b>Kin Cradle</b>  {b.CradleStatus}", _small);
            y += 18;
            if (b.CradleQueue.Count > 0)
            {
                var names = new StringBuilder();
                foreach (int id in b.CradleQueue) names.Append(w.FindUnitAny(id)?.Name).Append(' ');
                GUI.Label(new Rect(x, y, cw, 16), $"Kin-cores waiting: {names}  (each regrowth: {CostText(w, PlayerBase.RegrowCost)}, or twice as slow without)", _tiny);
                y += 16;
            }
            if (w.Caches.Count > 0)
            {
                if (GUI.Button(new Rect(x, y, 200, 22), "Recall kin-cores now", _button)) w.Issue(new PlayerOrder { Type = OrderType.RecallCores });
                GUI.Label(new Rect(x + 210, y + 3, cw - 210, 18), $"only when no Vhorn is left standing (happens on its own after {Balance.WipeRecallSeconds:0} s)", _tiny);
                y += 26;
            }
            y += 4;

            // Smelter
            GUI.Label(new Rect(x, y, cw, 18), $"<b>Smelter</b>  {b.SmelterStatus}   <size=10>2 ferrite -> 1 alloy every {Balance.SmeltSeconds:0}s</size>", _small);
            y += 26;

            // Salvage Bench
            GUI.Label(new Rect(x, y, cw, 18), $"<b>Salvage Bench</b>  {b.BenchStatus}", _small);
            y += 20;
            foreach (var sq in w.Squads)
            {
                GUI.Label(new Rect(x, y + 3, 110, 18), sq.Name, _tiny);
                bool arms = sq.WeaponTier < 2;
                bool plate = sq.ArmorTier < 2;
                if (GUI.Button(new Rect(x + 110, y, 130, 22), arms ? "Ferrite arms" : "Arms done", arms && w.Stock.Has(PlayerBase.WeaponsCost) ? _button : _buttonOff) && arms)
                    w.Issue(new PlayerOrder { Type = OrderType.CraftWeapons, TargetId = sq.Id });
                if (GUI.Button(new Rect(x + 246, y, 130, 22), plate ? "Ferrite plate" : "Plate done", plate && w.Stock.Has(PlayerBase.ArmorCost) ? _button : _buttonOff) && plate)
                    w.Issue(new PlayerOrder { Type = OrderType.CraftArmor, TargetId = sq.Id });
                y += 25;
            }
            GUI.Label(new Rect(x, y, cw, 16), $"Arms: {CostText(w, PlayerBase.WeaponsCost)}   Plate: {CostText(w, PlayerBase.ArmorCost)}", _tiny);
            y += 20;
            if (!b.SigilCrafted)
            {
                if (GUI.Button(new Rect(x, y, 200, 22), "Craft the Breach Sigil", w.Stock.Has(PlayerBase.SigilCost) ? _button : _buttonOff)) w.Issue(new PlayerOrder { Type = OrderType.CraftSigil });
                GUI.Label(new Rect(x + 210, y + 3, cw - 210, 18), CostText(w, PlayerBase.SigilCost) + "  (opens Breach Hold)", _tiny);
            }
            else GUI.Label(new Rect(x, y, cw, 18), "<color=#8fdc8f>Breach Sigil crafted: Breach Hold is open.</color>", _tiny);
        }

        // =====================================================================================
        // Help and victory
        // =====================================================================================

        private void DrawHelp()
        {
            var r = Panel(new Rect(W / 2 - 340, 60, 680, 660));
            GUI.Label(new Rect(r.x + 14, r.y + 10, 600, 24), "<b>KINFORGE</b>  MVP slice: The Craters", _title);
            string body =
                "You lead one kin-band of five Vhorn (Shieldbearer, Mender, Bruiser, Siegewright, Warwright) like a raid leader leads a party: " +
                "you decide <b>where they go and stand</b> and <b>which strategy</b> they fight under. They pick targets and abilities themselves, " +
                "spend their own mana and stamina, and they <b>learn</b>: Doctrine tiers unlock smarter play per strategy, Instinct tunes it fight by fight. The game never pauses.\n\n" +
                "<b>Move</b>  WASD or the left stick walks the kin-band (relative to the camera). Right-click ground to march there, an enemy to focus it, a resource to gather, a kin-core cache to recover, the base to go home.\n" +
                "<b>Camera</b>  drag with the mouse or use the right stick to look around; Q/E turn, wheel or triggers zoom, Space (right stick click) swings behind the kin-band. M opens the map of what you have explored.\n" +
                "<b>Strategies</b>  1 Charge, 2 Defend, 3 Hold, 4 Kite, 5 Focus Fire, 6 Spread Out, 7 Stack, 8 Fall Back, 9 Rest, 0 Fortify (gamepad: bumpers cycle). Hover the hotbar for what each does and risks.\n" +
                "<b>Other</b>  H go home, J garrison, K stop, Tab inspect the next Vhorn, F2 base, F3 battle reports. Gamepad: A gather/recover here, X focus the enemy ahead, Y go home, B fall back.\n\n" +
                "<b>Push your luck</b>  Every hit leaves a wound: the dark red end of a health bar is health that no heal brings back until the kin-band rests at home (garrison, J). " +
                "The further out you push, the lower everyone's ceiling gets. Mana (blue) pays for heals, walls, siege works and Titan Form; stamina (gold) for dodges and heavy blows. " +
                "Veterans keep a reserve; green Vhorn spend it all. Going home also banks Doctrine, deposits the haul, and runs the Smelter and Salvage Bench.\n\n" +
                "<b>Injury</b>  Downed once: up again Injured. Downed while Injured: killed; a squadmate carries the kin-core home and the Kin Cradle regrows it with Doctrine intact. " +
                "A wiped kin-band's cores come home on their own (slower to regrow if you can't pay).\n\n" +
                "<b>Titan Form</b>  At Doctrine tier 3 the Shieldbearer can grow to titan size for 15 s: a living wall that stops Rustmaw's charge and shelters kin from fire.\n\n" +
                "<b>Loop</b>  Venture out, fight and gather, come home before the wounds run too deep, craft gear and upgrade the War Hall, then open Breach Hold with the Sigil and kill <b>Rustmaw</b>.\n\n" +
                "<b>Playtest keys</b>  F7 drill to Doctrine tier 3, F9 game speed, F10 skip a Rustmaw phase, F11 reveal map.";
            GUI.Label(new Rect(r.x + 14, r.y + 40, r.width - 28, r.height - 80), body, _wrapSmall);
            if (GUI.Button(new Rect(r.x + r.width / 2 - 90, r.yMax - 36, 180, 26), "Start  (F1 toggles this)", _buttonOn)) _showHelp = false;
            if (GUI.Button(new Rect(r.x + 14, r.yMax - 36, 150, 26), "New random world", _button))
            {
                _showHelp = false;
                _g.StartWorld(Random.Range(1, 99999));
            }
        }

        private void DrawVictory(World w)
        {
            var r = Panel(new Rect(W / 2 - 260, H / 2 - 150, 520, 260));
            GUI.Label(new Rect(r.x, r.y + 18, r.width, 40), "RUSTMAW IS DEAD", _big);
            int kills = 0;
            foreach (var sq in w.Squads)
                foreach (var m in sq.Members) kills += m.Kills;
            string body = $"Biome 1, The Craters, is cleared in {Clock(w.VictoryAt)}.\n" +
                          $"Your kin-band made {kills} kills and fought {w.Reports.Count} battles.\n\n" +
                          "Its relic would unlock the Forge, Core Lathe and War Shrine and the road to the Fissures: that is the next milestone. " +
                          "Thanks for playtesting this slice!";
            GUI.Label(new Rect(r.x + 20, r.y + 64, r.width - 40, 130), body, _center);
            if (GUI.Button(new Rect(r.x + 60, r.yMax - 46, 180, 30), "Keep playing", _button)) _victoryDismissed = true;
            if (GUI.Button(new Rect(r.xMax - 240, r.yMax - 46, 180, 30), "New random world", _buttonOn)) _g.StartWorld(Random.Range(1, 99999));
        }
    }
}
