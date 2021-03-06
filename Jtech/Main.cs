﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using Oxide.Plugins.JtechCore;

namespace Oxide.Plugins {

	//PM.INSERT(PluginInfo, TheGreatJ, http://oxidemod.org/plugins/jpipes.2402/, https://github.com/jacobcoughenour/Jtech)
	class Jtech : RustPlugin {

		#region API

		// when any plugin loads (including Jtech)
		void OnPluginLoaded(Plugin plugin) {

			// call RegisterJDeployables hook for this plugin
			var r = plugin?.Call("RegisterJDeployables");

			// if the plugin didn't register any JDeployables
			if (!JDeployableManager.isPluginRegistered(plugin.Title))
				return;

			// load JDeployables from save data for this plugin
			JDeployableManager.LoadJDeployables(plugin.Title);
		}

		// when any plugin unloads (including Jtech)
		void OnPluginUnloaded(Plugin plugin) {
			if (!JDeployableManager.isPluginRegistered(plugin.Title))
				return;
			
			// save JDeployables from this plugin
			JDeployableManager.SaveJDeployables(plugin.Title);
			DataManager.Save();

			// unload spawned JDeployables from this plugin
			JDeployableManager.UnloadJDeployables(plugin.Title);

			// unregister JDeployable types from this plugin
			JDeployableManager.UnregisterJDeployables(plugin.Title);
		}

		/// <summary>
		/// JDeployable API
		/// <para/>Called when the plugin loads to register custom JDeployables with the JDeployableManager.
		/// </summary>
		void RegisterJDeployables() {
			JDeployableManager.RegisterJDeployable<JtechDeployables.TransportPipe>();
			JDeployableManager.RegisterJDeployable<JtechDeployables.Assembler>();
		}

		#endregion

		#region Oxide Hooks
		

		void OnServerInitialized() {
			
			NextFrame(() => {
				foreach (var player in BasePlayer.activePlayerList)
					UserInfo.Get(player);

				// start update
				timer.Repeat(0.25f, 0, JDeployableManager.Update);
			});
		}
		
		void Unload() {

			// Destroy UserInfo from all the players
			var users = UnityEngine.Object.FindObjectsOfType<UserInfo>();
			if (users != null) {
				foreach (UserInfo go in users) {
					go.DestroyCui();
					go.CancelPlacing();
					GameObject.Destroy(go);
				}
			}

			OnPluginUnloaded(this);
		}

		void OnNewSave(string filename) {
			//JDeployableManager.UnloadJDeployables();
			DataManager.Save();
		}

		void OnServerSave() {
			JDeployableManager.SaveAllJDeployables();
			DataManager.Save();
		}

		// removes anything named UserInfo from the player
		[ConsoleCommand("jtech.clean")]
		private void cmdjtechclean(ConsoleSystem.Arg arg) {

			List<UnityEngine.Object> uis = new List<UnityEngine.Object>();
			foreach (var player in BasePlayer.activePlayerList) {
				foreach (var c in player.GetComponents<Component>()) {
					if (c.GetType().ToString() == "Oxide.Plugins.JtechCore.UserInfo") {
						uis.Add(c);
					}
				}
			}

			foreach (var u in uis) {
				UnityEngine.Object.Destroy(u);
			}

			Puts($"{uis.Count} destroyed");

			NextFrame(() => {
				foreach (var player in BasePlayer.activePlayerList)
					UserInfo.Get(player);
			});
		}

		#region Player

		void OnPlayerSleepEnded(BasePlayer player) {
			// Add UserInfo to player
			UserInfo.Get(player);
		}

		void OnItemDeployed(Deployer deployer, BaseEntity entity) => UserInfo.Get(deployer?.GetOwnerPlayer())?.OnDeployPlaceholder(entity);

		void OnEntityBuilt(Planner planner, GameObject go) {
			BaseEntity entity = go?.GetComponent<BaseEntity>();
			if (entity != null)
				UserInfo.Get(planner?.GetOwnerPlayer())?.OnDeployPlaceholder(entity);
		}

		bool? CanMoveItem(Item item, PlayerInventory playerLoot, uint targetContainer, int targetSlot) {
			return UserInfo.Get(playerLoot.GetComponent<BasePlayer>())?.CanMoveItem(item, targetSlot);
		}

		#endregion

		#region Structure

		void OnHammerHit(BasePlayer player, HitInfo hit) {

			UserInfo.OnHammerHit(player, hit);
			hit.HitEntity?.GetComponent<JDeployable.Child>()?.parent.OnHammerHit(player, hit);

			//PM.DEBUGSTART
			ListComponentsDebug(player, hit.HitEntity);
			//PM.DEBUGEND
		}

		void OnStructureDemolish(BaseCombatEntity entity, BasePlayer player) => OnKilledChild((BaseEntity) entity);
		void OnEntityDeath(BaseCombatEntity entity, HitInfo info) => OnKilledChild((BaseEntity) entity);
		void OnEntityKill(BaseNetworkable entity) => OnKilledChild((BaseEntity) entity);

		void OnKilledChild(BaseEntity entity) {
			JDeployable.Child c = entity?.GetComponent<JDeployable.Child>();
			if (c != null && c.parent != null)
				KillDeployable(c.parent);
		}

		void KillDeployable(JDeployable deployable) {
			NextFrame(() => {
				deployable.Kill(BaseNetworkable.DestroyMode.Gib);
			});
		}

		bool? OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo) {

			if (entity != null && hitInfo != null) {

				JDeployable.Child c = entity?.GetComponent<JDeployable.Child>();
				if (c != null && c.parent != null) {

					if (true) // if nodecay
						hitInfo.damageTypes.Scale(Rust.DamageType.Decay, 0f); // no decay damage
					float damage = hitInfo.damageTypes.Total();
					if (damage > 0) {
						
						float newhealth = entity.health - damage;
						if (newhealth > 0f)
							c.parent.SetHealth(newhealth);
						else
							KillDeployable(c.parent);
					}
					return true;
				}
			}
			return null;
		}

		bool? CanPickupEntity(BaseCombatEntity entity, BasePlayer player) {
			JDeployable.Child c = entity?.GetComponent<JDeployable.Child>();
			if (c != null && c.parent != null && player != null)
				return c.parent.CanPickupEntity(c, player);
			return null;
		}

		void OnStructureRepair(BaseCombatEntity entity, BasePlayer player) {
			JDeployable.Child c = entity?.GetComponent<JDeployable.Child>();
			if (c != null && c.parent != null && player != null)
				NextTick(() => c.parent.OnStructureRepair(entity, player));
		}

		void OnStructureRotate(BaseCombatEntity entity, BasePlayer player) {
			entity?.GetComponent<JDeployable.Child>()?.parent.OnStructureRotate(entity, player);
		}

		bool? OnStructureUpgrade(BaseCombatEntity entity, BasePlayer player, BuildingGrade.Enum grade) {
			JDeployable.Child c = entity?.GetComponent<JDeployable.Child>();
			if (c != null && c.parent != null && player != null)
				return c.parent.OnStructureUpgrade(c, player, grade);
			return null;
		}


		#endregion

		#region Vending Machine

		bool? CanAdministerVending(VendingMachine machine, BasePlayer player) =>
			machine?.GetComponent<JDeployable.Child>()?.parent.CanAdministerVending(machine, player);

		bool? CanUseVending(VendingMachine machine, BasePlayer player) =>
			machine?.GetComponent<JDeployable.Child>()?.parent.CanUseVending(machine, player);

		bool? CanVendingAcceptItem(VendingMachine machine, Item item) =>
			machine?.GetComponent<JDeployable.Child>()?.parent.CanVendingAcceptItem(machine, item);

		object OnRotateVendingMachine(VendingMachine machine, BasePlayer player) =>
			machine?.GetComponent<JDeployable.Child>()?.parent.OnRotateVendingMachine(machine, player);

		void OnToggleVendingBroadcast(VendingMachine machine, BasePlayer player) =>
			machine?.GetComponent<JDeployable.Child>()?.parent.OnToggleVendingBroadcast(machine, player);

		#endregion


		#endregion

		[ChatCommand("jtech")]
		private void jtechmainchat(BasePlayer player, string cmd, string[] args) {
			UserInfo.ShowOverlay(player);
		}

		[ConsoleCommand("jtech.showoverlay")]
		private void showoverlay(ConsoleSystem.Arg arg) {
			UserInfo.ShowOverlay(arg.Player());
		}

		[ConsoleCommand("jtech.closeoverlay")]
		private void closeoverlay(ConsoleSystem.Arg arg) {
			UserInfo.HideOverlay(arg.Player());
		}

		[ConsoleCommand("jtech.closemenu")]
		private void closemenu(ConsoleSystem.Arg arg) {
			UserInfo.HideMenu(arg.Player());
		}

		[ConsoleCommand("jtech.menubutton")]
		private void menubutton(ConsoleSystem.Arg arg) {
			if (arg.HasArgs(2)) {
				UserInfo.HandleMenuButton(arg.Player(), arg.Args[0], arg.Args[1]);
			}
		}

		[ConsoleCommand("jtech.menuonoffbutton")]
		private void menuonoffbutton(ConsoleSystem.Arg arg) {
			if (arg.HasArgs()) {
				UserInfo.HandleMenuOnOffButton(arg.Player(), arg.Args[0]);
			}
		}

		[ConsoleCommand("jtech.startplacing")]
		private void startplacing(ConsoleSystem.Arg arg) {

			if (arg.HasArgs()) {

				Type deployabletype;
				if (JDeployableManager.TryGetType(arg.Args[0], out deployabletype)) {
					
					UserInfo.StartPlacing(arg.Player(), deployabletype);
				}
			}
		}

		//PM.DEBUGSTART

		// Lists the ent's components and variables to player's chat

		void ListComponentsDebug(BasePlayer player, BaseEntity ent) {

			List<string> lines = new List<string>();
			string s = "<color=#80c5ff>───────────────────────</color>";
			int limit = 1030;

			foreach (var c in ent.GetComponents<Component>()) {

				List<string> types = new List<string>();
				List<string> names = new List<string>();
				List<string> values = new List<string>();
				int typelength = 0;

				foreach (FieldInfo fi in c.GetType().GetFields()) {

					System.Object obj = (System.Object) c;
					string ts = fi.FieldType.Name;
					if (ts.Length > typelength)
						typelength = ts.Length;

					types.Add(ts);
					names.Add(fi.Name);

					var val = fi.GetValue(obj);
					if (val != null)
						values.Add(val.ToString());
					else
						values.Add("null");

				}

				if (s.Length > 0)
					s += "\n";
				s += types.Count > 0 ? "╔" : "═";
				s += $" {c.GetType()} : {c.GetType().BaseType}";
				//s += " <" + c.name + ">\n";

				for (int i = 0; i < types.Count; i++) {

					string ns = $"<color=#80c5ff> {types[i]}</color> {names[i]} = <color=#00ff00>{values[i]}</color>";

					if (s.Length + ns.Length >= limit) {
						lines.Add(s);
						s = "║" + ns;
					} else {
						s += "\n║" + ns;
					}
				}

				if (types.Count > 0) {
					s += "\n╚══";
					lines.Add(s);
					s = string.Empty;
				}
			}

			lines.Add(s);

			foreach (string ls in lines)
				PrintToChat(player, ls);

		}

		//PM.DEBUGEND
	}
}
