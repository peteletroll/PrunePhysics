using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PrunePhysics
{
	public class ModulePrunePhysics: PartModule
	{
		private static Regex[] whiteList = null;

		private static readonly string[] COMMENT = { "//", "#" };

		private const int UNKPHYSICS = -99999;

		private void loadWhiteList()
		{
			if (whiteList != null)
				return;

			List<Regex> wl = new List<Regex>();
			foreach (UrlDir.UrlFile url in GameDatabase.Instance.root.AllFiles) {
				// log("FILE " + url.fullPath);
				if (url.fileExtension != "ppwl")
					continue;
				string[] line = File.ReadAllLines(url.fullPath);
				for (int i = 0; i < line.Length; i++) {
					string[] ll = line[i].Split(COMMENT, 2, StringSplitOptions.None);
					if (ll == null || ll.Length <= 0)
						continue;
					string l = ll[0].Trim();
					if (l == "")
						continue;
					if (l[0] != '^')
						l = "^" + l;
					if (l[l.Length - 1] != '$')
						l = l + "$";
					log("REGEX " + l);
					Regex re = null;
					try {
						re = new Regex(l);
					} catch (Exception e) {
						log(url + "[" + (i + 1) + "]: " + e.Message);
					}
					if (re == null)
						continue;
					wl.Add(re);
				}
			}
			whiteList = wl.ToArray();
		}

		private bool checkWhiteList()
		{
			if (!part.gameObject)
				return false;

			List<PartModule> pms = part.FindModulesImplementing<PartModule>();
			if (pms == null)
				return true;

			for (int i = 0; i < pms.Count; i++) {
				PartModule pm = pms[i];
				if (!isInWhiteList(pm, true)) {
					log(desc(part) + ": " + pm.GetType() + " not in whitelist");
					return false;
				}
			}

			return true;
		}

		private bool isInWhiteList(Component c, bool verbose)
		{
			if (c == null)
				return true;
			return isInWhiteList(c.GetType().ToString(), verbose);
		}

		private bool isInWhiteList(string name, bool verbose)
		{
			loadWhiteList();
			int p = name.LastIndexOf('.');
			if (p > 0)
				name = name.Remove(0, p + 1);
			for (int i = 0; i < whiteList.Length; i++) {
				Regex re = whiteList[i];
				if (re == null)
					continue;
				if (re.IsMatch(name))
					return true;
			}
			if (verbose)
				log("name \"" + name + "\" is not in whitelist");
			return false;
		}

		[KSPField(isPersistant = true)]
		public int PhysicsSignificanceOrig = UNKPHYSICS;

		[UI_Toggle()]
		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true)]
		public bool PrunePhysics = false;
		private BaseField PrunePhysicsField = null;
		private bool prevPrunePhysics = false;

		private Part.PhysicalSignificance prevPhysicalSignificance = Part.PhysicalSignificance.FULL;

		public override void OnStart(StartState state)
		{
			base.OnStart(state);

			prevPhysicalSignificance = part.physicalSignificance;
			prevPrunePhysics = PrunePhysics;

			if (PhysicsSignificanceOrig == UNKPHYSICS) {
				PhysicsSignificanceOrig = part.PhysicsSignificance;
				log(desc(part, true) + ": PhysicsSignificanceOrig = " + PhysicsSignificanceOrig
					+ " at state " + state);
			}

			if (!HighLogic.LoadedSceneIsFlight)
				return;

			log(desc(part, true) + ".OnStart(" + state + ")");

			PrunePhysicsField = Fields[nameof(PrunePhysics)];

			if (PhysicsSignificanceOrig > 0 || !checkWhiteList()) {
				PrunePhysicsField.guiActive = PrunePhysicsField.guiActiveEditor = false;
#if !DEBUG
				enabled = false;
#endif
			}
		}

		public override void OnUpdate()
		{
			base.OnUpdate();

			if (MapView.MapIsEnabled || HighLogic.LoadedSceneIsEditor)
				return;

			if (PrunePhysicsField != null) {
				bool wantedPhysics = !PrunePhysics;
				bool actualPhysics = part.physicalSignificance == Part.PhysicalSignificance.FULL;
				PrunePhysicsField.guiName = nameof(PrunePhysics)
					+ (wantedPhysics != actualPhysics ? " (WAIT)" : "");
			}

			// log(desc(part) + ": PrunePhysics.OnUpdate()");
		}

		public void FixedUpdate()
		{
			if (HighLogic.LoadedSceneIsEditor)
				return;

			if (PrunePhysics != prevPrunePhysics) {
				prevPrunePhysics = PrunePhysics;
				AfterPrunePhysicsChange();
			}

			if (part.physicalSignificance != prevPhysicalSignificance) {
				log(desc(part, true) + ": " + prevPhysicalSignificance + " -> " + part.physicalSignificance
					+ " in " + HighLogic.LoadedScene);
				prevPhysicalSignificance = part.physicalSignificance;
			}
		}

		private void AfterPrunePhysicsChange() {
			log(desc(part) + ".PrunePhysics is now " + PrunePhysics);
			int newPhysicsSignificance = PrunePhysics ? 1 : 0;
			if (!part)
				return;
			changePhysics(part, newPhysicsSignificance);
			List<Part> scp = part.symmetryCounterparts;
			if (scp != null)
				for (int i = 0; i < scp.Count; i++)
					changePhysics(scp[i], newPhysicsSignificance);
		}

		private static void changePhysics(Part p, int newPhysicsSignificance)
		{
			if (!p)
				return;
			if (newPhysicsSignificance != p.PhysicsSignificance) {
				log(desc(p) + ".PhysicsSignificance " + p.PhysicsSignificance + " -> " + newPhysicsSignificance);
				p.PhysicsSignificance = newPhysicsSignificance;
			}
		}

#if DEBUG
		[KSPEvent(guiActive = true, guiActiveEditor = false)]
		public void ResetWhiteList()
		{
			whiteList = null;
			loadWhiteList();
		}
#endif

#if DEBUG
		[KSPEvent(guiActive = true, guiActiveEditor = false)]
		public void DumpPartPhysics()
		{
			string sep = new string('-', 16);
			log(sep + " " + desc(part, true) + " BEGIN " + sep);
			try {
				if (part) {
					log("SYMMETRY " + part.symMethod + " " + part.symmetryCounterparts.Count);
					log("PHYSICS " + part.physicalSignificance + " " + part.PhysicsSignificance);
					log("PARENT " + desc(part.parent, true));
					if (part.children != null) {
						for (int i = 0; i < part.children.Count; i++)
							log("CHILD [" + i + "] " + desc(part.children[i], true));
					} else {
						log("no children[]");
					}
					if (part.DragCubes != null) {
						if (part.DragCubes.Cubes != null) {
							List<DragCube> cc = part.DragCubes.Cubes;
							for (int i = 0; i < cc.Count; i++)
								log("CUBE [" + i + "] " + desc(cc[i]));
						} else {
							log("no DragCubes.Cubes");
						}
					} else {
						log("no DragCubes");
					}
					if (part.gameObject) {
						Component[] mb = part.gameObject.GetComponents<Component>();
						for (int i = 0; i < mb.Length; i++) {
							bool isMod = (mb[i] is PartModule);
							log("COMP [" + i + "] " + mb[i].GetInstanceID()
								+ " " + desc(mb[i].GetType())
								+ " " + (isMod ? isInWhiteList(mb[i], false).ToString() : "CMP"));
						}
					} else {
						log("no gameObject");
					}
				} else {
					log("no part");
				}
			} catch (Exception e) {
				log("EXCEPTION " + e.StackTrace);
			}
			log(sep + " " + desc(part) + " END " + sep);
		}
#endif

		private static string desc(Type t)
		{
			StringBuilder sb = new StringBuilder(t.ToString());
			t = t.BaseType;
			while (t != null) {
				sb.Append(" < ");
				if (sb.Length > 80) {
					sb.Append("...");
					break;
				}
				sb.Append(t.ToString());
				t = t.BaseType;
			}
			return sb.ToString();
		}

		private static string desc(Part p, bool withJoint = false)
		{
			if (!p)
				return "P:null";
			string name = p.name;
			int s = name.IndexOf(' ');
			if (s > 1)
				name = name.Remove(s);
			return "P:" + name + ":" + p.PhysicsSignificance + ":" + p.physicalSignificance
				+ ":" + p.flightID + (withJoint ? "[" + desc(p.attachJoint) + "]" : "");
		}

		private static string desc(PartJoint j)
		{
			if (!j)
				return "J:null";
			string m = j.joints.Count == 1 ? "" : "[" + j.joints.Count + "]";
			return "J:" + j.GetInstanceID() + m + "[" + desc(j.Host) + ">" + desc(j.Target) + "]";
		}

		private static string desc(DragCube c)
		{
			if (c == null)
				return "C:null";
			return "C:" + c.SaveToString();
		}

		private static void log(string msg)
		{
			Debug.Log("[PP:" + Time.frameCount + "] " + msg);
		}
	}
}
