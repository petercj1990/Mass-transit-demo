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
        // Squad cards
        // =====================================================================================

        private void DrawSquadCards(World w)
        {
            var sel = _g.Selection;
            float y = 34f;
            for (int i = 0; i < w.Squads.Count; i++)
            {
                var sq = w.Squads[i];
                bool selected = sel.IsSelected(sq.Id);
                var r = Panel(new Rect(8, y, CardW, CardH));
                Color c = Palette.Squad(sq.ColorIndex);
                if (selected)
                {
                    Swatch(new Rect(r.x, r.y, r.width, 2), c);
                    Swatch(new Rect(r.x, r.yMax - 2, r.width, 2), c);
                }
                if (GUI.Button(new Rect(r.x, r.y, r.width, 22), GUIContent.none, _hidden))
                {
                    if (Event.current.shift) sel.Toggle(sq.Id);
                    else sel.SelectOnly(sq.Id);
                    if (_lastCardClickSquad == sq.Id && Time.unscaledTime - _lastCardClickTime < 0.4f) _g.Commands.FocusSelection();
                    _lastCardClickSquad = sq.Id;
                    _lastCardClickTime = Time.unscaledTime;
                }
                Swatch(new Rect(r.x + 8, r.y + 7, 10, 10), c);
                GUI.Label(new Rect(r.x + 22, r.y + 2, 150, 20), $"<b>{i + 1}  {sq.Name}</b>", _label);
                GUI.Label(new Rect(r.x + 130, r.y + 3, 132, 20), $"<color=#{Hex(Palette.KinMark)}>{CommandInfo.Get(sq.Stance).Name}</color>", _smallRight);
                GUI.Label(new Rect(r.x + 8, r.y + 19, 250, 18), TaskText(w, sq), _tiny);

                float ry = r.y + 36;
                foreach (var m in sq.Members)
                {
                    var rowRect = new Rect(r.x + 4, ry - 1, r.width - 8, 15);
                    if (sel.InspectUnitId == m.Id) Swatch(rowRect, new Color(1f, 1f, 1f, 0.07f));
                    if (GUI.Button(rowRect, GUIContent.none, _hidden))
                    {
                        sel.InspectUnitId = m.Id;
                        if (!sel.IsSelected(sq.Id)) sel.SelectOnly(sq.Id);
                    }
                    _tinyBold.normal.textColor = c;
                    GUI.Label(new Rect(r.x + 8, ry - 2, 14, 16), RoleLetter(m.Role), _tinyBold);
                    GUI.Label(new Rect(r.x + 22, ry - 2, 70, 16), m.Name, _tiny);
                    string state;
                    Color hp;
                    switch (m.Life)
                    {
                        case LifeState.Dead:
                            state = m.Core == CoreState.AtCradle ? "<color=#ffb040>regrowing</color>" : "<color=#ff5040>dead</color>";
                            hp = Color.clear;
                            break;
                        case LifeState.Downed:
                            state = "<color=#ff7060>downed</color>";
                            hp = Color.clear;
                            break;
                        default:
                            state = m.Injured ? $"<color=#ffb040>injured</color>" : (m.Action == UnitAction.Resting ? "resting" : "");
                            hp = m.Injured ? new Color(0.95f, 0.65f, 0.2f) : new Color(0.35f, 0.85f, 0.4f);
                            break;
                    }
                    if (m.Life == LifeState.Active) Bar(new Rect(r.x + 92, ry + 4, 96, 6), m.HpFraction, hp, new Color(0f, 0f, 0f, 0.55f));
                    else Bar(new Rect(r.x + 92, ry + 4, 96, 6), 0f, hp, new Color(0.2f, 0.05f, 0.05f, 0.7f));
                    int tier = m.Doctrine.Tier(sq.Stance);
                    for (int t = 0; t < 5; t++) Swatch(new Rect(r.x + 193 + t * 5, ry + 5, 4, 4), t < tier ? Palette.KinMark : new Color(1f, 1f, 1f, 0.12f));
                    GUI.Label(new Rect(r.x + 220, ry - 2, 46, 16), state, _tiny);
                    ry += 15;
                }
                float load = sq.CarryLoad;
                float capy = Mathf.Max(1f, sq.CarryCapacity);
                Bar(new Rect(r.x + 8, r.yMax - 12, 120, 5), load / capy, new Color(0.8f, 0.7f, 0.4f), new Color(0f, 0f, 0f, 0.5f));
                string extra = $"carry {load:0}/{capy:0}";
                if (sq.CoresCarried > 0) extra += $"   <color=#{Hex(Palette.KinMark)}>{sq.CoresCarried} kin-core(s)</color>";
                GUI.Label(new Rect(r.x + 132, r.yMax - 18, 134, 16), extra, _tiny);
                y += CardH + 6;
            }
        }

        // =====================================================================================
        // Command bar
        // =====================================================================================

        private void DrawCommandBar(World w)
        {
            var sel = _g.Selection;
            if (sel.Squads.Count == 0)
            {
                var hint = Panel(new Rect(W / 2 - 230, H - 40, 460, 30));
                GUI.Label(new Rect(hint.x + 10, hint.y + 6, 440, 20), "Select a kin-band: click a Vhorn, drag a box, or press 1-6.", _small);
                return;
            }
            const int cols = 5;
            const float bw = 96f, bh = 38f, gap = 4f;
            float width = cols * (bw + gap) + 8 + 150;
            var r = Panel(new Rect(W / 2 - width / 2, H - (bh * 2 + gap + 38), width, bh * 2 + gap + 34));

            CommandType? shared = null;
            var names = new StringBuilder();
            foreach (int id in sel.Squads)
            {
                var sq = w.FindSquad(id);
                if (sq == null) continue;
                if (names.Length > 0) names.Append(", ");
                names.Append(sq.Name);
                if (shared == null) shared = sq.Stance;
                else if (shared != sq.Stance) shared = (CommandType)255;
            }
            GUI.Label(new Rect(r.x + 8, r.y + 4, width - 16, 20), $"<b>{names}</b>   <size=10>right-click: move / attack (focus) / gather / recover cores / return home</size>", _small);

            CommandInfo hovered = null;
            float by = r.y + 26;
            for (int i = 0; i < CommandInput.CommandForKey.Length; i++)
            {
                var cmd = CommandInput.CommandForKey[i];
                var info = CommandInfo.Get(cmd);
                var br = new Rect(r.x + 4 + (i % cols) * (bw + gap), by + (i / cols) * (bh + gap), bw, bh);
                bool on = shared.HasValue && shared.Value == cmd;
                if (GUI.Button(br, $"{info.Name}\n<size=10>[{info.Hotkey}]</size>", on ? _buttonOn : _button)) _g.Commands.IssueCommand(cmd);
                if (br.Contains(Event.current.mousePosition)) hovered = info;
            }
            float ox = r.x + 4 + cols * (bw + gap) + 4;
            if (GUI.Button(new Rect(ox, by, 142, 26), "Return home  <size=10>[H]</size>", _button)) _g.Commands.IssueSimple(OrderType.ReturnToBase);
            if (GUI.Button(new Rect(ox, by + 28, 142, 26), "Garrison  <size=10>[J]</size>", _button)) _g.Commands.IssueSimple(OrderType.Garrison);
            if (GUI.Button(new Rect(ox, by + 56, 142, 24), "Stop  <size=10>[K]</size>", _button)) _g.Commands.IssueSimple(OrderType.Stop);

            if (hovered != null)
            {
                string tip = $"<b>{hovered.Name}</b> [{hovered.Hotkey}]\n{hovered.Does}\n<color=#c8b48c>Warwright:</color> {hovered.Warwright}\n<color=#c8b48c>Siegewright:</color> {hovered.Siegewright}\n<color=#8fdc8f>Strong vs:</color> {hovered.StrongAgainst}\n<color=#ff8f80>Risk:</color> {hovered.Risk}";
                float th = _tooltip.CalcHeight(new GUIContent(tip), 380);
                GUI.Label(new Rect(r.x, r.y - th - 6, 380, th), tip, _tooltip);
            }
        }

        // =====================================================================================
        // Objectives
        // =====================================================================================

        private void DrawObjectives(World w)
        {
            float x = W - 336, y = 34;
            if (!_showObjectives)
            {
                var small = Panel(new Rect(x, y, 328, 24));
                if (GUI.Button(small, "Objectives  (show)", _hidden)) _showObjectives = true;
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
                (b != null && b.WarHallLevel >= 2 && w.Squads.Count >= 2, "Upgrade the War Hall and recruit a 2nd kin-band"),
                (gear, "Garrison a squad to smelt alloy; craft ferrite gear"),
                (b != null && b.SigilCrafted, "Craft the Breach Sigil to open Breach Hold"),
                (w.Victory, "Defeat Rustmaw (3 phases; bring 2 squads)"),
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
            float top = 34 + (_showObjectives ? 134 : 30);
            float height = Mathf.Min(H - top - 214, u.IsPlayer ? 560 : 300);
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
            GUI.Label(new Rect(x, y, cw, 18), $"{gear}   fights {u.Fights}   kills {u.Kills}", _tiny);
            y += 18;

            // Abilities
            GUI.Label(new Rect(x, y, cw, 18), "<b>Abilities</b> <size=10>(the unit decides when to use them)</size>", _small);
            y += 17;
            if (u.Abilities != null)
            {
                foreach (var s in u.Abilities)
                {
                    string txt = s.Unlocked
                        ? (s.Cooldown > 0f ? $"{s.Def.Name} <color=#999999>{s.Cooldown:0.0}s</color>" : $"{s.Def.Name} <color=#8fdc8f>ready</color>")
                        : $"<color=#777777>{s.Def.Name}: unlocks at Doctrine tier {Balance.ThirdAbilityTier}</color>";
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
                ? $"<color=#8fdc8f>{garrisons} squad(s) garrisoned</color>: the Smelter and Salvage Bench are running, and garrisoned Vhorn rest 3x faster."
                : "<color=#ffb040>No squad garrisoned.</color> Workshops only run with a squad garrisoned. Select a squad and press [J].", _wrapTiny);
            y += 30;

            // War Hall
            GUI.Label(new Rect(x, y, cw, 18), $"<b>War Hall</b>  level {b.WarHallLevel}: squad cap {b.SquadCap} ({w.Squads.Count} kin-bands)", _small);
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
                GUI.Label(new Rect(x, y, cw, 16), $"Kin-cores waiting: {names}  (each regrowth: {CostText(w, PlayerBase.RegrowCost)})", _tiny);
                y += 16;
            }
            bool canRecruit = w.Squads.Count < b.SquadCap && !b.Recruiting && w.Stock.Has(PlayerBase.RecruitCost);
            if (GUI.Button(new Rect(x, y, 200, 22), "Recruit a kin-band", canRecruit ? _button : _buttonOff)) w.Issue(new PlayerOrder { Type = OrderType.Recruit });
            GUI.Label(new Rect(x + 210, y + 3, cw - 210, 18), CostText(w, PlayerBase.RecruitCost) + (w.Squads.Count >= b.SquadCap ? "  <color=#ff7060>(cap reached)</color>" : ""), _tiny);
            y += 26;
            if (w.Caches.Count > 0)
            {
                if (GUI.Button(new Rect(x, y, 200, 22), "Recall kin-cores (last resort)", _button)) w.Issue(new PlayerOrder { Type = OrderType.RecallCores });
                GUI.Label(new Rect(x + 210, y + 3, cw - 210, 18), CostText(w, PlayerBase.RecallCost) + "  only when no Vhorn is left standing", _tiny);
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
            var r = Panel(new Rect(W / 2 - 330, 70, 660, 560));
            GUI.Label(new Rect(r.x + 14, r.y + 10, 600, 24), "<b>KINFORGE</b>  MVP slice: The Craters", _title);
            string body =
                "You are an unseen commander. Your kin-bands of five Vhorn (Shieldbearer, Mender, Bruiser, Siegewright, Warwright) " +
                "fight on their own. You set <b>intent</b>: where to go and which combat command to fight under. They pick targets, " +
                "positions and abilities themselves, and they <b>learn</b>: Doctrine tiers unlock smarter behaviour per command, Instinct tunes it fight by fight. " +
                "The game never pauses.\n\n" +
                "<b>Camera</b>  WASD / arrows pan, Q/E rotate, mouse wheel zoom, middle-drag pan (Alt+middle-drag rotates), Space centres on the selection, F8 edge-scroll.\n" +
                "<b>Select</b>  click a Vhorn or drag a box; 1-6 pick a kin-band (double-tap to jump); Tab cycles; shift adds.\n" +
                "<b>Orders</b>  right-click ground: move.  Enemy: attack it (Focus target).  Resource: gather and haul home.  Base: return and deposit.  Kin-core cache: recover.\n" +
                "<b>Combat commands</b>  Z Charge, X Defensive Formation, C Hold Position, V Kite, B Focus Fire, N Spread Out, M Stack, G Fall Back, T Fortify, R Rest. Hover the buttons for what each does and risks.\n" +
                "<b>Other</b>  H return home, J garrison, K stop.  F2 base, F3 battle reports, Esc closes panels.\n\n" +
                "<b>Injury</b>  A Vhorn dropped once is Downed and gets up Injured (-20% health and speed). Rest clears it. Dropped again while Injured: killed. " +
                "A squadmate carries its kin-core home; the Kin Cradle regrows it with Doctrine and Instinct intact (unbanked XP is lost). A full wipe drops a core cache.\n\n" +
                "<b>Goal</b>  Gather ferrite and hide, garrison a squad to run the Smelter and Salvage Bench, upgrade the War Hall for a second kin-band, craft ferrite gear and the " +
                "Breach Sigil, then take two squads into Breach Hold (north) and kill <b>Rustmaw</b>.\n\n" +
                "<b>Playtest keys</b>  F9 game speed x1/x2/x4, F10 skip Rustmaw to its next phase, F11 reveal map.";
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
                          $"Your kin-bands made {kills} kills and fought {w.Reports.Count} battles.\n\n" +
                          "Its relic would unlock the Forge, Core Lathe and War Shrine and the road to the Fissures: that is the next milestone. " +
                          "Thanks for playtesting this slice!";
            GUI.Label(new Rect(r.x + 20, r.y + 64, r.width - 40, 130), body, _center);
            if (GUI.Button(new Rect(r.x + 60, r.yMax - 46, 180, 30), "Keep playing", _button)) _victoryDismissed = true;
            if (GUI.Button(new Rect(r.xMax - 240, r.yMax - 46, 180, 30), "New random world", _buttonOn)) _g.StartWorld(Random.Range(1, 99999));
        }
    }
}
