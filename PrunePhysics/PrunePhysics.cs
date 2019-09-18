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

		private readonly string[] COMMENT = { "//", "#" };

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

		private BaseEvent prunePhysicsEvent;
		private BaseEvent forcePhysicsEvent;

		[KSPField(isPersistant = true)]
		public int PhysicsSignificanceOrig = UNKPHYSICS;

		[KSPField(isPersistant = true)]
		public int PhysicsSignificanceWanted = UNKPHYSICS;

		public override void OnStart(StartState state)
		{
			if (PhysicsSignificanceOrig == UNKPHYSICS) {
				PhysicsSignificanceOrig = PhysicsSignificanceWanted = part.PhysicsSignificance;
				log(desc(part, true) + ": PhysicsSignificanceOrig = " + PhysicsSignificanceOrig);
			}

			if (!HighLogic.LoadedSceneIsFlight)
				return;

			log(desc(part) + ".OnStart(" + state + ")");

			base.OnStart(state);

			loadWhiteList();

			prunePhysicsEvent = Events["PrunePhysics"];
			forcePhysicsEvent = Events["ForcePhysics"];
		}

		private Part.PhysicalSignificance lastPhys = Part.PhysicalSignificance.FULL;

		public override void OnUpdate()
		{
			base.OnUpdate();

			if (part.physicalSignificance != lastPhys) {
				log(desc(part) + ": " + lastPhys + " -> " + part.physicalSignificance
					+ " in " + HighLogic.LoadedScene);
				lastPhys = part.physicalSignificance;
			}

			if (MapView.MapIsEnabled || HighLogic.LoadedSceneIsEditor)
				return;

			bool hp = hasPhysics(part);
			if (prunePhysicsEvent != null)
				prunePhysicsEvent.guiActive = hp;
			if (forcePhysicsEvent != null)
				forcePhysicsEvent.guiActive = !hp;

			// log(desc(part) + ": PrunePhysics.OnUpdate()");
		}

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
						for (int i = 0; i < mb.Length; i++)
							log("COMP [" + i + "] " + mb[i].GetInstanceID() + " " + desc(mb[i].GetType()));
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

		[KSPEvent(guiActive = true, guiActiveEditor = false)]
		public void PrunePhysics()
		{
			if (!part)
				return;
			prunePhysics(part);
			List<Part> scp = part.symmetryCounterparts;
			if (scp != null)
				for (int i = 0; i < scp.Count; i++)
					prunePhysics(scp[i]);
		}

		[KSPEvent(guiActive = true, guiActiveEditor = false)]
		public void ForcePhysics()
		{
			if (!part)
				return;
			forcePhysics(part);
			List<Part> scp = part.symmetryCounterparts;
			if (scp != null)
				for (int i = 0; i < scp.Count; i++)
					forcePhysics(scp[i]);
		}

		public static bool hasPhysics(Part part)
		{
			if (!part)
				return false;
			bool ret = (part.physicalSignificance == Part.PhysicalSignificance.FULL);
			if (HighLogic.LoadedSceneIsFlight && ret != part.rb) {
				log(desc(part) + ": hasPhysics() Rigidbody incoherency: "
					+ part.physicalSignificance + ", " + (part.rb ? "rb ok" : "rb null"));
				ret = part.rb;
			}
			return ret;
		}

		public static bool prunePhysics(Part part)
		{
			if (!part || !hasPhysics(part))
				return false;

			log(desc(part) + ": prunePhysics() unsupported yet");
			return false;
		}

		public static bool forcePhysics(Part part)
		{
			if (!part || hasPhysics(part))
				return false;

			log(desc(part) + ": calling PromoteToPhysicalPart(), "
				+ part.physicalSignificance + ", " + part.PhysicsSignificance);
			part.PromoteToPhysicalPart();
			log(desc(part) + ": called PromoteToPhysicalPart(), "
				+ part.physicalSignificance + ", " + part.PhysicsSignificance);
			if (part.parent) {
				if (part.attachJoint) {
					log(desc(part) + ": parent joint exists already: " + desc(part.attachJoint));
				} else {
					AttachNode nodeHere = part.FindAttachNodeByPart(part.parent);
					AttachNode nodeParent = part.parent.FindAttachNodeByPart(part);
					AttachModes m = (nodeHere != null && nodeParent != null) ?
						AttachModes.STACK : AttachModes.SRF_ATTACH;
					part.CreateAttachJoint(m);
					log(desc(part) + ": created joint " + m + " " + desc(part.attachJoint));
				}
			}

			return true;
		}

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
			return "P:" + name + ":" + p.flightID + (withJoint ? "[" + desc(p.attachJoint) + "]" : "");
		}

		private static string desc(PartJoint j)
		{
			if (!j)
				return "J:null";
			return "J:" + j.GetInstanceID() + "[" + desc(j.Host) + ">" + desc(j.Target) + "]";
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
