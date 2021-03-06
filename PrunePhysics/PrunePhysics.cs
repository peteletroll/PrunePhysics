using UnityEngine;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using KSP.UI.Screens.DebugToolbar;

namespace PrunePhysics
{
	public class ModulePrunePhysics: PartModule
	{
		[KSPField(isPersistant = true)]
		public int Revision = -1;

		private static int _revision = -1;
		private static int getRevision()
		{
			if (_revision < 0) {
				_revision = 0;
				try {
					_revision = Assembly.GetExecutingAssembly().GetName().Version.Revision;
				} catch (Exception e) {
					string sep = new string('-', 80);
					log(sep);
					log("Exception reading revision:\n" + e.StackTrace);
					log(sep);
				}
			}
			return _revision;
		}

		private void checkRevision()
		{
			int r = getRevision();
			if (Revision != r) {
				log(nameof(PrunePhysics) + ": REVISION " + Revision + " -> " + r);
				Revision = r;
			}
		}

		private static Regex[] whiteList = null;

		private static readonly string[] COMMENT = { "//", "#" };

		private static readonly string[] whiteListExtension = {
			"prunephysicswhitelist",
			"ppwl"
		};

		private static bool isWhiteListFile(UrlDir.UrlFile url)
		{
			string ext = url.fileExtension.ToLowerInvariant();
			for (int i = 0; i < whiteListExtension.Length; i++)
				if (ext == whiteListExtension[i])
					return true;
			return false;
		}

		private void loadWhiteList()
		{
			if (whiteList != null)
				return;

			List<Regex> wl = new List<Regex>();
			foreach (UrlDir.UrlFile url in GameDatabase.Instance.root.AllFiles) {
				// log("FILE " + url.fullPath);
				if (!isWhiteListFile(url))
					continue;
				string[] line = File.ReadAllLines(url.fullPath);
				for (int i = 0; i < line.Length; i++) {
					string[] ll = line[i].Split(COMMENT, 2, StringSplitOptions.None);
					if (ll == null || ll.Length <= 0)
						continue;
					string l = ll[0].Trim();
					Regex re = makeRegex(l, true);
					if (re == null)
						continue;
					wl.Add(re);
				}
			}
			whiteList = wl.ToArray();
		}

		private static Regex makeRegex(string re, bool anchor)
		{
			Regex ret = null;
			if (re.Length <= 0)
				return null;
			if (anchor) {
				if (re[0] != '^')
					re = "^" + re;
				if (re[re.Length - 1] != '$')
					re = re + "$";
			}
			// log("REGEX " + re);
			try {
				ret = new Regex(re);
			} catch (Exception e) {
				log(re + ": " + e.Message);
			}
			return ret;
		}

		private bool checkWhiteList()
		{
			if (!part.gameObject)
				return false;

			string[] c = whiteListCheckStrings();
			for (int i = 0; i < c.Length; i++)
				if (!isInWhiteList(c[i], true, part))
					return false;

			return true;
		}

		private string[] whiteListCheckStrings()
		{
			List<string> ret = new List<string>();

			List<PartModule> pml = part.FindModulesImplementing<PartModule>();
			if (pml != null)
				for (int i = 0; i < pml.Count; i++)
					if (pml[i])
						ret.Add(pml[i].GetType().ToString());

			PartResourceList prl = part.Resources;
			if (prl != null)
				for (int i = 0; i < prl.Count; i++)
					if (prl[i] != null)
						ret.Add("Resource." + prl[i].resourceName);

			return ret.ToArray();
		}

		private bool isInWhiteList(string fullname, bool verbose, Part part = null)
		{
			loadWhiteList();
			string name = fullname;
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
				log((part ? desc(part) + ": " : "") + "name \"" + fullname + "\" is not in whitelist");
			return false;
		}

		// these are used only in debug mode
		private static bool PrunePhysicsEnabled = true;
		private void UpdateTogglePrunePhysicsEnabledGui()
		{
			BaseEvent TogglePrunePhysicsEnabledEvent = Events["TogglePrunePhysicsEnabled"];
			if (TogglePrunePhysicsEnabledEvent != null)
				TogglePrunePhysicsEnabledEvent.guiName = (PrunePhysicsEnabled ? "Disable" : "Enable")
					+ " PrunePhysics Globally";
		}

		private const int PhysicsSignificanceDefault = -1;

		[KSPField(isPersistant = true)]
		public int PhysicsSignificanceOrig = PhysicsSignificanceDefault;

		[UI_Toggle(affectSymCounterparts = UI_Scene.All)]
		[KSPField(guiName = "PrunePhysics", isPersistant = true, guiActive = true, guiActiveEditor = true)]
		public bool PrunePhysics = false;
		private BaseField PrunePhysicsField = null;

		private Part.PhysicalSignificance prevPhysicalSignificance = Part.PhysicalSignificance.FULL;

		private bool canPrunePhysics()
		{
			string failMsg = "";
			if (!part) {
				failMsg = "no part";
			} else if (part.partInfo == null || part.partInfo.partConfig == null) {
				failMsg = "no partConfig";
			} else if (!PrunePhysicsEnabled) {
				failMsg = "disabled globally";
			} else if (part.sameVesselCollision) {
				failMsg = "sameVesselCollision is true";
			} else if (PhysicsSignificanceOrig > 0) {
				failMsg = "originally physicsless";
			} else if (part.isVesselEVA) {
				failMsg = "is EVA";
			} else if (!checkWhiteList()) {
				failMsg = "whitelist check failed";
			} else if (!part.parent) {
				if (HighLogic.LoadedSceneIsFlight) {
					failMsg = "is root in flight";
				} else {
					log(desc(part) + ".canPrunePhysics(): root part, but not in flight");
				}
			}
			if (failMsg != "") {
				log(desc(part) + ".canPrunePhysics() returns false: " + failMsg);
				return false;
			}
			log(desc(part) + ".canPrunePhysics() returns true");
			return true;
		}

		private static char[] commandSeparators = { ' ', '\t' };

		public static void consoleCommand(string arg)
		{
			string[] args = arg.Split(commandSeparators, StringSplitOptions.RemoveEmptyEntries);
			// log("CMD START");
			/*
			for (int i = 0; i < args.Length; i++)
				log("CMD[" + i + "] '" + args[i] + "'");
			*/
			Vessel v = FlightGlobals.ActiveVessel;
			if (!HighLogic.LoadedSceneIsFlight) {
				log("not in flight mode");
			} else if (!v) {
				log("no active vessel");
			} else if (args.Length == 2 && args[0] == "enable") {
				setEnabled(v, args[1], true);
			} else if (args.Length == 2 && args[0] == "disable") {
				setEnabled(v, args[1], false);
			} else {
				log("illegal command");
			}
			// log("CMD END");
		}

		private static void setEnabled(Vessel v, string type, bool enabled)
		{
			MonoBehaviour[] mbs = allBehaviours(v, type);
			if (mbs == null)
				return;
			log("found " + mbs.Length + " " + type);
			int changed = 0;
			for (int i = 0; i < mbs.Length; i++) {
				if (mbs[i].enabled != enabled) {
					changed++;
					mbs[i].enabled = enabled;

				}
			}
			log("changed " + changed + " " + type);
		}

		private static MonoBehaviour[] allBehaviours(Vessel v, string type)
		{
			Regex re = makeRegex(type, false);
			if (re == null)
				return null;
			List<MonoBehaviour> ret = new List<MonoBehaviour>();
			Dictionary<string, int> found = new Dictionary<string, int>();
			for (int i = 0; i < v.parts.Count; i++) {
				Part p = v.parts[i];
				MonoBehaviour[] mbs = p.gameObject.GetComponents<MonoBehaviour>();
				for (int j = 0; j < mbs.Length; j++) {
					string t = mbs[j].GetType().ToString();
					if (re.IsMatch(t)) {
						if (!found.ContainsKey(t)) {
							log("found " + t);
							found.Add(t, 0);
						}
						ret.Add(mbs[j]);
					}
				}
			}
			return ret.ToArray();
		}

		private static bool consoleSetupDone = false;

		public override void OnStart(StartState state)
		{
			base.OnStart(state);
			enabled = false;
			checkRevision();

			if (!consoleSetupDone) {
				consoleSetupDone = true;
				DebugScreenConsole.AddConsoleCommand("pp", consoleCommand, "PrunePhysics commands");
			}

			if (part && part.partInfo != null && part.partInfo.partConfig != null)
				if (!part.partInfo.partConfig.TryGetValue("PhysicsSignificance", ref PhysicsSignificanceOrig))
					PhysicsSignificanceOrig = PhysicsSignificanceDefault;

			if (PhysicsSignificanceOrig <= 0 && part.PhysicsSignificance > 0)
				PrunePhysics = true;

			prevPhysicalSignificance = part.physicalSignificance;

			PrunePhysicsField = Fields[nameof(PrunePhysics)];

			UpdateTogglePrunePhysicsEnabledGui();

			bool cpp = canPrunePhysics();

			if (PrunePhysicsField != null)
				PrunePhysicsField.guiActive = PrunePhysicsField.guiActiveEditor = cpp;

			if (HighLogic.LoadedSceneIsFlight) {
				if (PrunePhysics && cpp) {
					log(desc(part) + ": PRUNING PHYSICS FROM ORIG=" + PhysicsSignificanceOrig
						+ " CUR=" + part.PhysicsSignificance);
					part.PhysicsSignificance = 1;
				} else {
					part.PhysicsSignificance = PhysicsSignificanceOrig;
				}
			}
		}

		private void FixedUpdate()
		{
			// this exists just to test GetMethod(), enabled is always false
		}

		public override void OnUpdate()
		{
			base.OnUpdate();

			if (!HighLogic.LoadedSceneIsFlight)
				return;

			if (part.physicalSignificance != prevPhysicalSignificance) {
				log(desc(part, true) + ": " + prevPhysicalSignificance + " -> " + part.physicalSignificance
					+ " in " + HighLogic.LoadedScene);
				prevPhysicalSignificance = part.physicalSignificance;
			}

			if (part.PartActionWindow && PrunePhysicsField != null) {
				bool wantedPhysics = !PrunePhysics;
				bool actualPhysics = (part.physicalSignificance == Part.PhysicalSignificance.FULL);
				string newGuiName = nameof(PrunePhysics)
					+ (wantedPhysics != actualPhysics ? " (WAIT)" : "");
				if (PrunePhysicsField.guiName != newGuiName) {
					log(desc(part) + ": guiName \"" + PrunePhysicsField.guiName + "\" -> \"" + newGuiName + "\"");
					PrunePhysicsField.guiName = newGuiName;
					MonoUtilities.RefreshContextWindows(part);
				}
			}
		}

#if DEBUG

		public class Stat
		{
			private Dictionary<string, int> s = new Dictionary<string, int>();

			public void inc(string k, int i = 1)
			{
				if (s.ContainsKey(k)) {
					s[k] += i;
				} else {
					s[k] = i;
				}
			}

			public void dump()
			{
				List<string> l = new List<string>();
				foreach (KeyValuePair<string, int> i in s)
					l.Add(i.Value.ToString("D4") + " " + i.Key);
				l.Sort();
				for (int i = 0; i < l.Count; i++) {
					log(l[i]);
				}
			}
		}

		const string DEBUGGROUP = "PrunePhysicsDebug";

		[KSPEvent(
			guiName = "Toggle PrunePhysics Globally",
			guiActive = true,
			guiActiveEditor = false,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public void TogglePrunePhysicsEnabled()
		{
			PrunePhysicsEnabled = !PrunePhysicsEnabled;
			UpdateTogglePrunePhysicsEnabledGui();
		}

		[KSPEvent(
			guiActive = true,
			guiActiveEditor = false,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public void ReplaceAttachJoint()
		{
			Part p = part;

			log("ReplaceAttachJoint(" + desc(p) + ")");
			AttachModes m = p.attachMode;
			log("attachMode: " + m);
			log("old attachJoint: " + desc(p.attachJoint));
			if (!p.attachJoint)
				return;
			Destroy(p.attachJoint);
			p.attachJoint = null;
			p.CreateAttachJoint(m);
			log("new attachJoint: " + desc(p.attachJoint));
		}

		[KSPEvent(
			guiActive = true,
			guiActiveEditor = false,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public void ResetWhiteList()
		{
			whiteList = null;
			loadWhiteList();
		}

		[KSPEvent(
			guiActive = true,
			guiActiveEditor = true,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public void ToggleShowAutostruts()
		{
			PhysicsGlobals.AutoStrutDisplay = !PhysicsGlobals.AutoStrutDisplay;
			if (HighLogic.LoadedSceneIsEditor)
				GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartTweaked, part);
		}

		[KSPEvent(
			guiActive = true,
			guiActiveEditor = false,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public void DumpPartPosition()
		{
			string sep = new string('-', 16);
			log(sep + " " + desc(part, true) + " BEGIN " + sep);
			string posFmt = "F6";
			log("ORG " + part.orgPos.ToString(posFmt) + " " + part.orgRot.ToString(posFmt));
			log("POS "
				+ part.vessel.rootPart.transform.InverseTransformPoint(part.transform.TransformPoint(Vector3.zero)).ToString(posFmt)
				+ " " +
				(Quaternion.Inverse(part.vessel.rootPart.transform.rotation) * part.transform.rotation).ToString(posFmt));
			log(sep + " " + desc(part, true) + " END " + sep);
		}

		[KSPEvent(
			guiActive = true,
			guiActiveEditor = false,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public void DumpPartPhysics()
		{
			string sep = new string('-', 16);
			log(sep + " " + desc(part, true) + " BEGIN " + sep);
			try {
				if (part) {
					log("SYMMETRY " + part.symMethod + " " + part.symmetryCounterparts.Count);
					log("PHYSICS " + part.physicalSignificance + " " + part.PhysicsSignificance);
					log("PARENT " + desc(part.parent, true));
					log("ATTACH " + desc(part.attachJoint));

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

					string[] c = whiteListCheckStrings();
					for (int i = 0; i < c.Length; i++)
						log("WLCS [" + i + "] " + c[i]
							+ " " + isInWhiteList(c[i], false));

					if (part.gameObject) {
						PartJoint[] pj = part.gameObject.GetComponents<PartJoint>();
						for (int i = 0; i < pj.Length; i++)
							log("JOINT " + desc(pj[i]));

						MonoBehaviour[] mb = part.gameObject.GetComponents<MonoBehaviour>();
						for (int i = 0; i < mb.Length; i++) {
							if (!mb[i])
								continue;
							log((mb[i] is PartModule ? "MOD" : "MBH")
								+ (mb[i].enabled ? ":E" : ":D")
								+ "[" + i + "] " + mb[i].GetInstanceID()
								+ " " + desc(mb[i].GetType()));
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

		[KSPEvent(
			guiActive = true,
			guiActiveEditor = false,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public void DumpPhysicsStats()
		{
			string sep = new string('-', 16);
			log(sep + " " + desc(part, true) + " BEGIN " + sep);
			try {
				if (!vessel) {
					log("no vessel");
				} else if (vessel.parts == null) {
					log("no vessel.parts");
				} else {
					log("ROOT " + desc(vessel.rootPart));
					Part[] pp = vessel.parts.ToArray();
					log(pp.Length + " parts");

					Dictionary<string, int> s = new Dictionary<string, int>();
					for (int i = 0; i < pp.Length; i++) {
						Part p = pp[i];
						if (!p) {
							incStat(s, "null");
							continue;
						}

						incStat(s, "PartJoints", p.gameObject.GetComponents<PartJoint>().Length);
						incStat(s, "ConfigurableJoints", p.gameObject.GetComponents<ConfigurableJoint>().Length);
						incStat(s, "Rigidbodies", p.gameObject.GetComponents<Rigidbody>().Length);

						incStat(s, "physicalSignificance = " + p.physicalSignificance);
						incStat(s, "PhysicsSignificance value = " + p.PhysicsSignificance);
						if (p.PhysicsSignificance > 0) {
							incStat(s, "PhysicsSignificance > 0");
						} else {
							incStat(s, "PhysicsSignificance <= 0");
						}

						ModulePrunePhysics mpp = p.FindModuleImplementing<ModulePrunePhysics>();
						if (!mpp) {
							incStat(s, "no mpp");
							continue;
						}

						incStat(s, "PhysicsSignificanceOrig value = " + mpp.PhysicsSignificanceOrig);
						if (mpp.PhysicsSignificanceOrig > 0) {
							incStat(s, "PhysicsSignificanceOrig > 0");
						} else {
							incStat(s, "PhysicsSignificanceOrig <= 0");
						}

						incStat(s, "PrunePhysics = " + mpp.PrunePhysics);
					}

					List<string> l = new List<string>();
					foreach (KeyValuePair<string, int> i in s) {
						l.Add(i.Key + ": " + i.Value);
					}
					l.Sort();
					for (int i = 0; i < l.Count; i++) {
						log(l[i]);
					}
				}
			} catch (Exception e) {
				log("EXCEPTION " + e.StackTrace);
			}
			log(sep + " " + desc(part) + " END " + sep);
		}

		[KSPEvent(
			guiActive = true,
			guiActiveEditor = false,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public void DumpMonoBehaviourStats()
		{
			string sep = new string('-', 16);
			string title = vessel.vesselName + " [" + vessel.parts.Count + "]";
			log(sep + " " + title + " BEGIN " + sep);
			try {
				if (!vessel) {
					log("no vessel");
				} else if (vessel.parts == null) {
					log("no vessel.parts");
				} else {
					BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
					Type[] noParams = new Type[0];

					Part[] pp = vessel.parts.ToArray();
					Stat ts = new Stat();
					Stat es = new Stat();
					Stat ds = new Stat();
					for (int i = 0; i < pp.Length; i++) {
						Part p = pp[i];
						if (!p || !p.gameObject)
							continue;

						MonoBehaviour[] mbs = p.gameObject.GetComponents<MonoBehaviour>();
						for (int j = 0; j < mbs.Length; j++) {
							MonoBehaviour mb = mbs[j];
							Type t = mb.GetType();
							string q = "{"
								+ (mb.enabled ? "E" : "D")
								+ (t.GetMethod("Update", f) == null ? ":U" : "")
								+ (t.GetMethod("FixedUpdate", f) == null ? ":FU" : "")
								+ "}";
							ts.inc("total");
							ts.inc("total " + q);
							if (mb.enabled) {
								es.inc(t + " " + q);
							} else {
								ds.inc(t + " " + q);
							}
						}
					}

					ds.dump();
					es.dump();
					ts.dump();
				}
			} catch (Exception e) {
				log("EXCEPTION " + e.StackTrace);
			}
			log(sep + " " + title + " END " + sep);
		}

		private static void incStat(Dictionary<string, int> s, string k, int i = 1)
		{
			if (s.ContainsKey(k)) {
				s[k] += i;
			} else {
				s[k] = i;
			}
		}

		[KSPEvent(
			guiActive = true,
			guiActiveEditor = false,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public void CycleAllAutoStrut()
		{
			log("CycleAllAutoStrut()");
			vessel.CycleAllAutoStrut();
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
			string ot = (j == j.Host.attachJoint || j == j.Target.attachJoint) ? "" : "OT:";
			return "J:" + ot + j.GetInstanceID() + m + "[" + desc(j.Host) + ">" + desc(j.Target) + "]";
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
