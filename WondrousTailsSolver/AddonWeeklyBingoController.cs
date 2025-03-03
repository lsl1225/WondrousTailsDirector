﻿using System;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiLib.Classes;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using Lumina.Excel.Sheets;

namespace WondrousTailsSolver;

public unsafe class AddonWeeklyBingoController : IDisposable {
    private readonly IAddonEventHandle?[] eventHandles = new IAddonEventHandle?[16];

    private readonly NineGridNode?[] currentDutyBorder = new NineGridNode[16];
    
    public AddonWeeklyBingoController() {
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "WeeklyBingo", OnAddonSetup);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "WeeklyBingo", OnAddonFinalize);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "WeeklyBingo", OnAddonRefresh);
    }
    
    public void Dispose() {
        Service.AddonLifecycle.UnregisterListener(OnAddonSetup, OnAddonFinalize, OnAddonRefresh);

        var addon = (AddonWeeklyBingo*) Service.GameGui.GetAddonByName("WeeklyBingo");
        if (addon is not null) {
            ResetEventHandles();
            RemoveDutySlots(addon);
        }
    }
    
    private void OnAddonSetup(AddonEvent type, AddonArgs args) {
        var addonWeeklyBingo = (AddonWeeklyBingo*)args.Addon;

        // Reset any event handles
        ResetEventHandles();
        
        // Register new event handles
        foreach (var index in Enumerable.Range(0, 16)) {
            var dutySlot = addonWeeklyBingo->DutySlotList[index];
        
            eventHandles[index] = Service.AddonEventManager.AddEvent((nint) addonWeeklyBingo, (nint) dutySlot.DutyButton->OwnerNode, AddonEventType.ButtonClick, OnDutySlotClick);
        }
        
        // Add new custom border nodes to ui
        AddBorderNodes(addonWeeklyBingo);
    }
    
    private void OnAddonFinalize(AddonEvent type, AddonArgs args) {
        ResetEventHandles();
        RemoveDutySlots((AddonWeeklyBingo*) args.Addon);
    }
    
    private void OnAddonRefresh(AddonEvent type, AddonArgs args) {
        foreach (var index in Enumerable.Range(0, 16)) {
            ref var borderNode = ref currentDutyBorder[index];
            if (borderNode is null) continue;
            
            borderNode.IsVisible = IsCurrentDuty(index);

            // Re-adjust positions, there may be a race condition moving things around
            var dutySlot = ((AddonWeeklyBingo*)args.Addon)->DutySlotList[index];
            var buttonNode = dutySlot.DutyButton->OwnerNode;
            if (buttonNode is null) continue;

            borderNode.Position = new Vector2(buttonNode->X, buttonNode->Y);
        }
    }

    private void AddBorderNodes(AddonWeeklyBingo* addonWeeklyBingo) {
        foreach (var index in Enumerable.Range(0, 16)) {
            var dutySlot = addonWeeklyBingo->DutySlotList[index];
            var buttonNode = dutySlot.DutyButton->OwnerNode;

            var newBorderNode = new SimpleNineGridNode {
                Size = new Vector2(buttonNode->GetWidth(), buttonNode->GetHeight()) + new Vector2(0.0f, 4.0f),
                Position = new Vector2(buttonNode->GetXFloat(), buttonNode->GetYFloat()),
                Color = System.Configuration.CurrentDutyColor,
                NodeID = dutySlot.ResNode1->NodeId + 100,
                TexturePath = "ui/uld/WeeklyBingo_hr1.tex",
                TextureCoordinates = new Vector2(1.0f, 1.0f),
                TextureSize = new Vector2(72.0f, 48.0f),
                IsVisible = IsCurrentDuty(index),
            };
                
            currentDutyBorder[index] = newBorderNode;
            System.NativeController.AttachToAddon(newBorderNode, (AtkUnitBase*)addonWeeklyBingo, (AtkResNode*)buttonNode, NodePosition.AfterTarget);
        }
    }

    private void OnDutySlotClick(AddonEventType atkEventType, IntPtr atkUnitBase, IntPtr atkResNode) {
        var dutyButtonNode = (AtkResNode*) atkResNode;
        var tileIndex = (int)dutyButtonNode->NodeId - 12;

        var selectedTask = PlayerState.Instance()->GetWeeklyBingoTaskStatus(tileIndex);
        var bingoData = PlayerState.Instance()->WeeklyBingoOrderData[tileIndex];
        
        if (selectedTask is PlayerState.WeeklyBingoTaskStatus.Open) {
            var dutiesForTask = WondrousTailsTaskResolver.GetTerritoriesFromOrderId(Service.DataManager, bingoData);

            var territoryType = dutiesForTask.FirstOrDefault();
            var cfc = Service.DataManager.GetExcelSheet<ContentFinderCondition>().FirstOrDefault(cfc => cfc.TerritoryType.RowId == territoryType);
            if (cfc.RowId is 0) return;

            AgentContentsFinder.Instance()->OpenRegularDuty(cfc.RowId);
        }
    }

    private void RemoveDutySlots(AddonWeeklyBingo* addonWeeklyBingo) {
        foreach (var index in Enumerable.Range(0, 16)) {
            ref var borderNode = ref currentDutyBorder[index];
            if (borderNode is null) continue;
       
            System.NativeController.DetachFromAddon(borderNode, (AtkUnitBase*)addonWeeklyBingo);
            borderNode.Dispose();
            borderNode = null;
        }
    }

    private void ResetEventHandles() {
        foreach (var index in Enumerable.Range(0, 16)) {
            if (eventHandles[index] is {} handle) {
                Service.AddonEventManager.RemoveEvent(handle);
                eventHandles[index] = null;
            }
        }
    }

    private bool IsCurrentDuty(int index) {
        if (!Service.Condition.Any(ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95)) return false;

        var currentTaskId = PlayerState.Instance()->WeeklyBingoOrderData[index];
        var taskList = WondrousTailsTaskResolver.GetTerritoriesFromOrderId(Service.DataManager, currentTaskId);
        
        return taskList.Contains(Service.ClientState.TerritoryType);
    }
}