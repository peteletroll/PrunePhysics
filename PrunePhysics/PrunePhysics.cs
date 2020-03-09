using UnityEngine;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

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

		private const int UNKPHYSICS = -99999;

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

		[KSPField(isPersistant = true)]
		public int PhysicsSignificanceOrig = UNKPHYSICS;

		[UI_Toggle()]
		[KSPField(guiName = "PrunePhysics", isPersistant = true, guiActive = true, guiActiveEditor = true)]
		public bool PrunePhysics = false;
		private BaseField PrunePhysicsField = null;
		private bool prevPrunePhysics = false;

		private BaseEvent TogglePrunePhysicsEnabledEvent = null;
		private static bool PrunePhysicsEnabled = true;

		private Part.PhysicalSignificance prevPhysicalSignificance = Part.PhysicalSignificance.FULL;

		private bool canPrunePhysics()
		{
			string failMsg = "";
			if (!part) {
				failMsg = "no part";
			} else if (!PrunePhysicsEnabled) {
				failMsg = "disabled globally";
			} else if (PhysicsSignificanceOrig > 0) {
				failMsg = "already physicsless";
			} else if (!checkWhiteList()) {
				failMsg = "whitelist check failed";
			} else if (!part.parent) {
				if (HighLogic.LoadedSceneIsEditor) {
					log(desc(part) + ".canPrunePhysics(): root part, but in editor");
				} else {
					failMsg = "is root in flight";
				}
			}
			if (failMsg != "") {
				log(desc(part) + ".canPrunePhysics() returns false: " + failMsg);
				return false;
			}
			log(desc(part) + ".canPrunePhysics() returns true");
			return true;
		}

		public override void OnAwake()
		{
			log(desc(part, true) + ".OnAwake() in scene " + HighLogic.LoadedScene);

			base.OnAwake();
		}

		public override void OnStart(StartState state)
		{
			log(desc(part, true) + ".OnStart(" + state + ") in scene " + HighLogic.LoadedScene);

			base.OnStart(state);

			doSetup(state.ToString());
		}

		private void doSetup(string state)
		{
			PrunePhysicsField = Fields[nameof(PrunePhysics)];
			TogglePrunePhysicsEnabledEvent = Events["TogglePrunePhysicsEnabled"];

			setPrunePhysics(true);

			checkRevision();

			prevPhysicalSignificance = part.physicalSignificance;
			prevPrunePhysics = PrunePhysics;
			if (PhysicsSignificanceOrig == UNKPHYSICS) {
				PhysicsSignificanceOrig = part.PhysicsSignificance;
				log(desc(part, true) + ": PhysicsSignificanceOrig = " + PhysicsSignificanceOrig
					+ " at state " + state);
			}

			bool cpp = canPrunePhysics();

			if (PhysicsSignificanceOrig > 0 || !cpp)
				setPrunePhysics(false);

			if (!HighLogic.LoadedSceneIsFlight)
				return;

			if (PrunePhysics && cpp) {
				log(desc(part) + ": PRUNING PHYSICS FROM ORIG=" + PhysicsSignificanceOrig
					+ " CUR=" + part.PhysicsSignificance);
				part.PhysicsSignificance = 1;
			}
		}

		private void setPrunePhysics(bool flag)
		{
			enabled = flag;
			if (PrunePhysicsField != null)
				PrunePhysicsField.guiActive = PrunePhysicsField.guiActiveEditor = flag;
		}

		public override void OnUpdate()
		{
			base.OnUpdate();

			if (MapView.MapIsEnabled || HighLogic.LoadedSceneIsEditor
				|| !part || !part.PartActionWindow)
				return;

			if (PrunePhysicsField != null) {
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

			if (TogglePrunePhysicsEnabledEvent != null)
				TogglePrunePhysicsEnabledEvent.guiName = (PrunePhysicsEnabled ? "Disable" : "Enable")
					+ " PrunePhysics Globally";
		}

		public void FixedUpdate()
		{
			if (HighLogic.LoadedSceneIsEditor || !part)
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
			changePhysics(part, newPhysicsSignificance);
			List<Part> scp = part.symmetryCounterparts;
			if (scp == null)
				return;
			for (int i = 0; i < scp.Count; i++) {
				Part p = scp[i];
				if (p == part)
					continue;
				ModulePrunePhysics mpp = p.FindModuleImplementing<ModulePrunePhysics>();
				if (mpp)
					mpp.PrunePhysics = PrunePhysics;
			}
		}

		private static void changePhysics(Part p, int newPhysicsSignificance)
		{
			log(desc(p) + ".changePhysics(" + newPhysicsSignificance + ")");
			if (!p || !p.parent)
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

		[KSPEvent(
			guiName = "Toggle PrunePhysics Globally",
			guiActive = true,
			guiActiveEditor = false
		)]
		public void TogglePrunePhysicsEnabled()
		{
			PrunePhysicsEnabled = !PrunePhysicsEnabled;
		}

		[KSPEvent(guiActive = true, guiActiveEditor = true)]
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
						Component[] mb = part.gameObject.GetComponents<Component>();
						for (int i = 0; i < mb.Length; i++) {
							if (!mb[i] || mb[i] is PartModule)
								continue;
							log("COMP [" + i + "] " + mb[i].GetInstanceID()
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

		[KSPEvent(guiActive = true, guiActiveEditor = true)]
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

		private static void incStat(Dictionary<string, int> d, string k, int i = 1)
		{
			if (d.ContainsKey(k)) {
				d[k] += i;
			} else {
				d[k] = i;
			}
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
