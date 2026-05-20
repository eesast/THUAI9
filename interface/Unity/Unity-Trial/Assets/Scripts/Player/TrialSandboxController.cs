using System;
using System.Collections.Generic;
using System.Linq;
using Protobuf;
using THUAI9.Unity.Core;
using THUAI9.Unity.Render;
using UnityEngine;

namespace THUAI9.Unity.Player
{
    /// <summary>
    /// Local THUAI7-style trial sandbox for the WebGL Trial entry.
    ///
    /// This deliberately models only the experience loop (select -> move -> action -> immediate
    /// feedback) and keeps the official THUAI9 server as the source of truth for competition rules.
    /// </summary>
    public sealed class TrialSandboxController : MonoBehaviour
    {
        private const int MapRows = 50;
        private const int MapCols = 50;
        private const int TrialTeamCount = 2;
        private const int FrameDurationMs = 50;
        private const int CharacterHp = 150;
        private const int DroneHp = 100;
        private const int CarHp = 100;
        private const int CharacterAttackRange = 1000;
        private const int CharacterSpeed = 5200;
        private const int ResourceMaxAmount = 500;
        private const int FactoryHp = 100;
        private const int InitialMaterial = 0;
        private const int InitialComputePower = 100;
        private const int MaxCharactersPerTeam = 3;
        private const int CharacterCreateCost = 50;
        private const int RepairComputeCost = 2;
        private const int RepairAmount = 60;
        private const int HarvestAmount = 40;
        private const int OccupyScore = 150;
        private const int InteractionRangeCells = 1;

        private const float MoveStepIntervalMs = 140f;
        private const int GoodsTransferAmount = 1;
        private const int FactoryBaseStorage = 5;
        private const int TechMaxLevel = 2;
        private static readonly int[][] OfficialMap = new int[][]
        {
            new int[] { 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 3, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 2, 3, 3, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 1, 2, 4, 2, 2, 2, 3, 3, 3, 3, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 3, 2, 2, 2, 1, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 4, 2, 3, 4, 4, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 5, 2, 2, 2, 6, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 3, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 2, 2, 3 },
            new int[] { 3, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 6, 3, 2, 4, 2, 2, 2, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 2, 2, 3 },
            new int[] { 3, 4, 4, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 3, 2, 4, 2, 2, 2, 2, 3, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 5, 2, 4, 4, 2, 2, 2, 2, 3 },
            new int[] { 3, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 7, 2, 2, 2, 2, 2, 2, 5, 2, 4, 4, 2, 2, 2, 2, 2, 2, 5, 3, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 4, 4, 4, 2, 2, 6, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 4, 4, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 4, 4, 4, 4, 4, 2, 2, 3, 3, 3, 3, 3, 3, 2, 2, 2, 2, 3, 2, 2, 4, 4, 3, 3, 2, 2, 2, 3, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 2, 3 },
            new int[] { 3, 2, 2, 4, 4, 4, 2, 4, 4, 2, 2, 2, 2, 3, 4, 3, 2, 2, 2, 2, 2, 3, 3, 2, 3, 2, 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 3, 2, 2, 3, 2, 2, 3 },
            new int[] { 3, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 3, 2, 3, 2, 2, 3, 4, 3, 3, 3, 2, 3, 3, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 2, 2, 2, 3, 2, 2, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 4, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 4, 4, 4, 2, 2, 2, 2, 2, 2, 4, 4, 7, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 2, 2, 4, 4, 4, 2, 2, 4, 2, 2, 2, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 5, 3, 3, 3, 2, 2, 3 },
            new int[] { 3, 2, 2, 7, 2, 2, 3, 2, 2, 2, 2, 6, 2, 2, 3, 3, 2, 2, 2, 2, 5, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 2, 2, 2, 2, 4, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 2, 2, 2, 2, 4, 4, 2, 4, 2, 2, 3, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 7, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 4, 2, 4, 4, 2, 2, 2, 4, 4, 2, 4, 2, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 3, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 4, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 3, 3, 3, 3, 2, 2, 2, 4, 2, 5, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 4, 4, 2, 7, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 4, 4, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 4, 4, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 7, 2, 4, 4, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 5, 2, 4, 2, 2, 2, 3, 3, 3, 3, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 4, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 3, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 2, 4, 2, 4, 4, 2, 2, 2, 4, 4, 2, 4, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 7, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 3, 2, 2, 4, 2, 4, 4, 2, 2, 2, 2, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 4, 2, 2, 2, 2, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 5, 2, 2, 2, 2, 3, 3, 2, 2, 6, 2, 2, 2, 2, 3, 2, 2, 7, 2, 2, 3 },
            new int[] { 3, 2, 2, 3, 3, 3, 5, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 2, 2, 2, 4, 2, 2, 4, 4, 4, 2, 2, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 7, 4, 4, 2, 2, 2, 2, 2, 2, 4, 4, 4, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 4, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 2, 2, 3, 2, 2, 2, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 3, 3, 2, 3, 3, 3, 4, 3, 2, 2, 3, 2, 3, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 3 },
            new int[] { 3, 2, 2, 3, 2, 2, 3, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 3, 2, 2, 2, 2, 3, 2, 3, 2, 3, 3, 2, 2, 2, 2, 2, 3, 4, 3, 2, 2, 2, 2, 4, 4, 2, 4, 4, 4, 2, 2, 3 },
            new int[] { 3, 2, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 3, 2, 2, 2, 3, 3, 4, 4, 2, 2, 3, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 2, 2, 4, 4, 4, 4, 4, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 4, 4, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 2, 6, 2, 2, 4, 4, 4, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 3, 5, 2, 2, 2, 2, 2, 2, 4, 4, 2, 5, 2, 2, 2, 2, 2, 2, 7, 2, 2, 2, 2, 2, 2, 4, 4, 4, 4, 3 },
            new int[] { 3, 2, 2, 2, 2, 4, 4, 2, 5, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 3, 2, 2, 2, 2, 4, 2, 3, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 4, 4, 3 },
            new int[] { 3, 2, 2, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 2, 2, 2, 4, 2, 3, 6, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 3 },
            new int[] { 3, 2, 2, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2, 3, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 6, 2, 2, 2, 5, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 4, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 4, 4, 3, 2, 4, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 1, 2, 2, 2, 3, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 3, 3, 3, 3, 2, 2, 2, 4, 2, 1, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 3, 3, 2, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 4, 3, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 4, 4, 4, 4, 2, 2, 2, 2, 2, 2, 2, 3 },
            new int[] { 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 }
        };

        private static readonly GoodsType[] GoodsOrder =
        {
            GoodsType.Semiconductor,
            GoodsType.Medicine,
            GoodsType.Toys,
            GoodsType.Clothes,
            GoodsType.Food
        };

        private static readonly TrialGoodsRule[] GoodsRules =
        {
            new TrialGoodsRule(GoodsType.Semiconductor, "semiconductor", "半导体", 10, 80),
            new TrialGoodsRule(GoodsType.Medicine, "medicine", "药品", 5, 50),
            new TrialGoodsRule(GoodsType.Toys, "toys", "小商品", 1, 8),
            new TrialGoodsRule(GoodsType.Clothes, "clothes", "服饰", 8, 32),
            new TrialGoodsRule(GoodsType.Food, "food", "食品", 3, 6)
        };

        private static readonly TrialTechRule[] TechRules =
        {
            new TrialTechRule("upgrade-hp", "Robust", "生命上限", 30),
            new TrialTechRule("upgrade-attack", "Warrior", "攻击能力", 60),
            new TrialTechRule("upgrade-attack-size", "AttackSize", "攻击范围", 60),
            new TrialTechRule("upgrade-robust", "Robust", "耐久防御", 30),
            new TrialTechRule("upgrade-move-speed", "MoveSpeed", "移动速度", 40),
            new TrialTechRule("upgrade-carry", "Carry", "携带容量", 50),
            new TrialTechRule("upgrade-efficiency", "Efficiency", "采集/占领效率", 40),
            new TrialTechRule("upgrade-production", "Production", "生产效率", 60),
            new TrialTechRule("upgrade-storage", "Storage", "工厂仓储", 50),
            new TrialTechRule("upgrade-price", "Price", "出售价格", 80),
            new TrialTechRule("upgrade-cost", "Cost", "降低成本", 50)
        };
        private readonly Dictionary<long, TrialTeam> teams = new Dictionary<long, TrialTeam>();
        private readonly Dictionary<long, TrialFactory> factories = new Dictionary<long, TrialFactory>();
        private readonly Dictionary<long, TrialResource> resources = new Dictionary<long, TrialResource>();
        private readonly Dictionary<long, TrialComputeCenter> computeCenters = new Dictionary<long, TrialComputeCenter>();
        private readonly Dictionary<long, TrialMarket> markets = new Dictionary<long, TrialMarket>();
        private readonly Dictionary<long, TrialCharacter> characters = new Dictionary<long, TrialCharacter>();
        private readonly HashSet<long> activeFactoryCells = new HashSet<long>();
        private TrialSelection currentSelection = TrialSelection.None;

        private float elapsedMs;
        private float lastSubmittedElapsedMs;
        private bool running;
        private long nextGuid = 1000;
        private long playerTeamId = 1;
        private long playerId = 1;
        private int sideFlag = 1;
        private string lastOptionsJson;
        private long activeCharacterGuid;

        public string StatusText { get; private set; } = "试玩：未启动";

        private void Awake()
        {
            if (FindObjectsOfType<TrialSandboxController>().Length > 1)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (!running)
            {
                return;
            }

            float deltaMs = Time.deltaTime * 1000f;
            elapsedMs += deltaMs;
            AdvanceCharacterPaths(deltaMs);
            HandleKeyboardShortcuts();

            if (elapsedMs - lastSubmittedElapsedMs >= FrameDurationMs)
            {
                SubmitFrame();
                lastSubmittedElapsedMs = elapsedMs;
            }
        }
        public void StartTrial(string optionsJson = null)
        {
            if (optionsJson != null)
            {
                lastOptionsJson = optionsJson;
            }

            ApplyOptions(lastOptionsJson);
            ResetState();
            BuildWorld();
            StatusText = "试玩已启动：两支队伍都可手动操控，请先点击任一队伍工厂创建角色";
            FrameSourceHub.Reset(FrameSourceHub.SourceKind.Trial, "本地试玩", StatusText);
            running = true;
            SubmitFrame();
        }

        public void ResetTrial()
        {
            StartTrial(lastOptionsJson);
        }
        public void StopTrial()
        {
            running = false;
            StatusText = "试玩：已停止";
            FrameSourceHub.Reset(FrameSourceHub.SourceKind.None, "试玩停止", StatusText);
        }

        public void SetSelection(WorldObjectInfo info, Vector2Int? tile)
        {
            currentSelection = BuildSelection(info, tile);
            if (currentSelection.Kind == TrialObjectKind.Character
                && characters.TryGetValue(currentSelection.Guid, out TrialCharacter character)
                && character.Hp > 0)
            {
                activeCharacterGuid = currentSelection.Guid;
            }
        }
        public string BuildSelectionText(WorldObjectInfo info, Vector2Int? tile)
        {
            TrialSelection selection = BuildSelection(info, tile);
            switch (selection.Kind)
            {
                case TrialObjectKind.Character:
                    if (characters.TryGetValue(selection.Guid, out TrialCharacter character))
                    {
                        return $"选中角色：队伍 {character.TeamId} / 玩家 {character.PlayerId}\n类型：{FormatCharacterType(character.Type)}  生命：{character.Hp}/{GetCharacterMaxHp(character.Type)}\n状态：{FormatCharacterState(character.State)}  位置：({character.Row}, {character.Col})\n载重：{GetCurrentLoad(character)}/{GetCharacterCarryCapacity(character)}  携带：{FormatInventory(character.GoodsLoad)}";
                    }
                    return $"选中角色：队伍 {selection.TeamId}\n位置：({selection.Row}, {selection.Col})";
                case TrialObjectKind.Factory:
                    if (factories.TryGetValue(selection.Guid, out TrialFactory factory))
                    {
                        return $"选中工厂：队伍 {factory.TeamId}\n血量：{factory.Hp}/{FactoryHp}  原料：{factory.Source}\n库存：{FormatInventory(factory.ProductInventory)}\n位置：({factory.Row}, {factory.Col})";
                    }
                    return $"选中工厂：队伍 {selection.TeamId}";
                case TrialObjectKind.Resource:
                    if (resources.TryGetValue(selection.Guid, out TrialResource resource))
                    {
                        return $"选中资源点\n剩余原料：{resource.Amount}/{ResourceMaxAmount}\n位置：({resource.Row}, {resource.Col})";
                    }
                    return $"选中资源点：({selection.Row}, {selection.Col})";
                case TrialObjectKind.ComputeCenter:
                    if (computeCenters.TryGetValue(selection.Guid, out TrialComputeCenter center))
                    {
                        string owner = center.OwnerTeamId <= 0 ? "未占领" : "队伍 " + center.OwnerTeamId;
                        return $"选中算力中心\n归属：{owner}  进度：{center.OccupyProgress}%\n位置：({center.Row}, {center.Col})";
                    }
                    return $"选中算力中心：({selection.Row}, {selection.Col})";
                case TrialObjectKind.Market:
                    if (markets.TryGetValue(selection.Guid, out TrialMarket market))
                    {
                        return $"选中市场：{FormatMarketType(market.MarketType)}\n位置：({market.Row}, {market.Col})\n价格：{FormatMarketPrices(market)}";
                    }
                    return $"选中市场：({selection.Row}, {selection.Col})";
                case TrialObjectKind.Tile:
                    return $"选中地图格：({selection.Row}, {selection.Col})\n类型：{FormatPlaceType(GetPlaceType(selection.Row, selection.Col))}";
                default:
                    return "未选中对象\n左键选择工厂/角色/资源/算力中心/市场；试玩中队伍 1 与队伍 2 均可操控。";
            }
        }
        public bool CanExecuteAction(string action, WorldObjectInfo info, Vector2Int? tile)
        {
            return GetVisibleActions(BuildSelection(info, tile)).Contains(NormalizeAction(action));
        }

        public List<string> GetVisibleActions(WorldObjectInfo info, Vector2Int? tile)
        {
            return GetVisibleActions(BuildSelection(info, tile));
        }

        public string GetActionHint(WorldObjectInfo info, Vector2Int? tile)
        {
            TrialSelection selection = BuildSelection(info, tile);
            switch (selection.Kind)
            {
                case TrialObjectKind.Factory:
                    return "工厂上下文：可创建三类角色、生产五类商品、升级 THUAI9 科技；两队工厂都可操控。";
                case TrialObjectKind.Character:
                    return "角色上下文：WASD 单格移动；采集/占领/攻击/交易会先寻路靠近目标，停止可打断。";
                case TrialObjectKind.Resource:
                    return "资源上下文：先选中一个角色，再点击采集；角色会自动走到资源九宫格内。";
                case TrialObjectKind.ComputeCenter:
                    return "算力中心上下文：无人机/机器人可占领，无人车会给出不合法原因。";
                case TrialObjectKind.Market:
                    return "市场上下文：角色靠近后可买入/卖出五类商品；价格按 THUAI9 基础价与市场倍率简化。";
                case TrialObjectKind.Tile:
                    return "地图格上下文：点击移动会让当前选中角色寻路过去；未选角色会提示先选对象。";
                default:
                    return "请点击地图对象后显示对应操作；初始两队都可手动试玩。";
            }
        }
        public void ExecuteSelectedAction(string action)
        {
            ExecuteAction(action, currentSelection);
        }

        public void HandleAction(string action)
        {
            ExecuteAction(action, currentSelection);
        }

        public void MoveSelectedOrPlayerToTile(int row, int col)
        {
            if (!running)
            {
                return;
            }

            if (!TryGetActiveCharacter(out TrialCharacter character))
            {
                StatusText = "请先选中一个对象（角色），再移动到目标地格";
                return;
            }

            StartMoveToTile(character, row, col, "右键移动");
            SubmitFrame();
        }
        private void ResetState()
        {
            elapsedMs = 0f;
            lastSubmittedElapsedMs = -FrameDurationMs;
            nextGuid = 1000;
            activeCharacterGuid = 0;
            currentSelection = TrialSelection.None;
            teams.Clear();
            factories.Clear();
            resources.Clear();
            computeCenters.Clear();
            markets.Clear();
            characters.Clear();
            activeFactoryCells.Clear();
            StatusText = "试玩：初始化中";
        }

        private void ApplyOptions(string optionsJson)
        {
            playerTeamId = 1;
            playerId = 1;
            sideFlag = 1;
            if (string.IsNullOrWhiteSpace(optionsJson))
            {
                return;
            }

            try
            {
                TrialOptions options = JsonUtility.FromJson<TrialOptions>(optionsJson);
                if (options != null)
                {
                    playerTeamId = Mathf.Clamp(options.teamId <= 0 ? 1 : options.teamId, 1, TrialTeamCount);
                    playerId = Math.Max(1, options.characterPlayerId);
                    sideFlag = options.sideFlag == 0 ? 1 : options.sideFlag;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Trial options ignored: " + ex.Message);
            }
        }

        private void BuildWorld()
        {
            for (int teamId = 1; teamId <= TrialTeamCount; teamId++)
            {
                teams[teamId] = new TrialTeam(teamId)
                {
                    Material = InitialMaterial,
                    ComputePower = InitialComputePower
                };
            }

            List<Vector2Int> factoryCells = new List<Vector2Int>();
            for (int row = 0; row < MapRows; row++)
            {
                for (int col = 0; col < MapCols; col++)
                {
                    PlaceType place = (PlaceType)OfficialMap[row][col];
                    if (place == PlaceType.Factory)
                    {
                        factoryCells.Add(new Vector2Int(row, col));
                    }
                    else if (place == PlaceType.Resource)
                    {
                        long resourceId = NextGuid();
                        resources[resourceId] = new TrialResource(resourceId, row, col)
                        {
                            Amount = ResourceMaxAmount
                        };
                    }
                    else if (place == PlaceType.ComputeCenter)
                    {
                        long centerId = NextGuid();
                        computeCenters[centerId] = new TrialComputeCenter(centerId, row, col);
                    }
                    else if (place == PlaceType.Market)
                    {
                        long marketId = NextGuid();
                        markets[marketId] = new TrialMarket(marketId, row, col, GetDefaultMarketType(row, col));
                    }
                }
            }

            Vector2Int teamOneFactory = factoryCells.Count > 0 ? factoryCells[0] : new Vector2Int(3, 3);
            Vector2Int teamTwoFactory = factoryCells.Count > 1 ? factoryCells[factoryCells.Count - 1] : new Vector2Int(MapRows - 4, MapCols - 4);
            AddFactory(1, 1, teamOneFactory);
            AddFactory(2, 2, teamTwoFactory);
        }

        private void AddFactory(long factoryId, long teamId, Vector2Int cell)
        {
            factories[factoryId] = new TrialFactory(factoryId, teamId, cell.x, cell.y);
            activeFactoryCells.Add(CellKey(cell.x, cell.y));
        }

        public void CreateCharacter()
        {
            CreateCharacter(CharacterType.Robot, currentSelection);
        }

        public void CreateCharacter(CharacterType type)
        {
            CreateCharacter(type, currentSelection);
        }

        private void CreateCharacter(CharacterType type, TrialSelection selection)
        {
            TrialFactory factory = ResolveFactory(selection, requirePlayerTeam: false);
            if (factory == null)
            {
                StatusText = "请先左键选中队伍工厂，再创建角色";
                return;
            }

            if (!teams.TryGetValue(factory.TeamId, out TrialTeam team))
            {
                StatusText = "试玩：该工厂不属于当前双队伍试玩";
                return;
            }

            int existingCount = characters.Values.Count(c => c.TeamId == factory.TeamId && c.Hp > 0);
            if (existingCount >= MaxCharactersPerTeam)
            {
                StatusText = $"队伍 {factory.TeamId} 已有 {MaxCharactersPerTeam} 个角色，达到试玩上限";
                return;
            }

            if (team.ComputePower < CharacterCreateCost)
            {
                StatusText = $"队伍 {factory.TeamId} 算力不足：创建角色需要 {CharacterCreateCost}";
                return;
            }

            long nextPlayerId = NextAvailablePlayerId(factory.TeamId);
            if (nextPlayerId <= 0)
            {
                StatusText = $"队伍 {factory.TeamId} 没有可用 PlayerID";
                return;
            }

            Vector2Int spawn = FindSpawnCellNearFactory(factory);
            activeCharacterGuid = NextGuid();
            characters[activeCharacterGuid] = new TrialCharacter(activeCharacterGuid, factory.TeamId, nextPlayerId, type, spawn.x, spawn.y)
            {
                Hp = GetCharacterMaxHp(type),
                State = CharacterState.Idle
            };
            team.ComputePower -= CharacterCreateCost;
            team.Score += 20;
            StatusText = $"已在队伍 {factory.TeamId} 工厂创建 {FormatCharacterType(type)}（PlayerID {nextPlayerId}）";
            SubmitFrame();
        }

        private void ExecuteAction(string rawAction, TrialSelection selection)
        {
            if (!running)
            {
                StartTrial(lastOptionsJson);
            }

            string action = NormalizeAction(rawAction);
            if (TryParseGoodsAction(action, "produce", out GoodsType produceGoods))
            {
                Produce(selection, produceGoods);
                SubmitFrame();
                return;
            }

            if (TryParseGoodsAction(action, "load", out GoodsType loadGoods))
            {
                LoadGoods(selection, loadGoods);
                SubmitFrame();
                return;
            }

            if (TryParseGoodsAction(action, "buy", out GoodsType buyGoods))
            {
                TradeGoods(selection, buyGoods, buy: true);
                SubmitFrame();
                return;
            }

            if (TryParseGoodsAction(action, "sell", out GoodsType sellGoods))
            {
                TradeGoods(selection, sellGoods, buy: false);
                SubmitFrame();
                return;
            }

            if (TryGetTechRule(action, out TrialTechRule techRule))
            {
                UpgradeTech(selection, techRule);
                SubmitFrame();
                return;
            }

            switch (action)
            {
                case "reset-trial":
                    ResetTrial();
                    break;
                case "create-drone":
                    CreateCharacter(CharacterType.Drone, selection);
                    break;
                case "create":
                case "create-robot":
                    CreateCharacter(CharacterType.Robot, selection);
                    break;
                case "create-car":
                    CreateCharacter(CharacterType.AutonomousCar, selection);
                    break;
                case "move":
                    ExecuteMove(selection);
                    break;
                case "up":
                    MoveBy(-1, 0);
                    break;
                case "down":
                    MoveBy(1, 0);
                    break;
                case "left":
                    MoveBy(0, -1);
                    break;
                case "right":
                    MoveBy(0, 1);
                    break;
                case "harvest":
                    Harvest(selection);
                    break;
                case "occupy":
                    Occupy(selection);
                    break;
                case "produce":
                    Produce(selection, GoodsType.Food);
                    break;
                case "upgrade":
                    UpgradeTech(selection, TechRules[0]);
                    break;
                case "attack":
                    Attack(selection);
                    break;
                case "recover":
                    Recover(selection);
                    break;
                case "stop":
                    StopCurrentAction();
                    break;
                default:
                    StatusText = "未知试玩动作：" + rawAction;
                    break;
            }

            SubmitFrame();
        }
        private void ExecuteMove(TrialSelection selection)
        {
            if (!TryGetActiveCharacter(out TrialCharacter character))
            {
                StatusText = "请先选中一个对象（角色），再执行移动";
                return;
            }

            if (!selection.HasPosition)
            {
                StatusText = "请先点击一个地图格或对象，再执行移动";
                return;
            }

            if (selection.Kind == TrialObjectKind.Tile && IsCellAvailableForCharacter(character, selection.Row, selection.Col))
            {
                StartMoveToTile(character, selection.Row, selection.Col, "移动至选中地格");
                return;
            }

            if (selection.Kind == TrialObjectKind.Tile)
            {
                StatusText = "该地格不可通行：THUAI9 中只能进入空地/草丛；资源、工厂、市场、算力中心需移动到附近交互";
                return;
            }

            MoveCharacterNear(character, selection.Row, selection.Col, "正在移动到选中对象附近，可点击停止打断", null);
        }

        private void MoveBy(int dRow, int dCol)
        {
            if (!TryGetSelectedCharacter(out TrialCharacter character))
            {
                StatusText = "请先左键选中一个已创建角色";
                return;
            }

            int row = Mathf.Clamp(character.Row + dRow, 0, MapRows - 1);
            int col = Mathf.Clamp(character.Col + dCol, 0, MapCols - 1);
            if (!IsCellAvailableForCharacter(character, row, col))
            {
                StatusText = $"键盘移动失败：({row}, {col}) 不可通行或已有角色";
                return;
            }

            CancelPlannedMove(character);
            character.Row = row;
            character.Col = col;
            character.State = CharacterState.Moving;
            StatusText = $"键盘移动到 ({row}, {col})";
        }

        private bool StartMoveToTile(TrialCharacter character, int row, int col, string verb)
        {
            row = Mathf.Clamp(row, 0, MapRows - 1);
            col = Mathf.Clamp(col, 0, MapCols - 1);
            if (!IsCellAvailableForCharacter(character, row, col))
            {
                StatusText = $"{verb}失败：({row}, {col}) 不可通行或已有角色";
                return false;
            }

            List<Vector2Int> path = FindPath(character, new List<Vector2Int> { new Vector2Int(row, col) });
            if (path == null)
            {
                StatusText = $"{verb}失败：没有可达路径";
                return false;
            }

            BeginPath(character, path, null, $"{verb}：正在前往 ({row}, {col})，可点击停止打断");
            return true;
        }
        private void Harvest(TrialSelection selection)
        {
            if (!TryGetActiveCharacter(out TrialCharacter character))
            {
                StatusText = "请先创建并选中角色再采集";
                return;
            }

            TrialResource resource = ResolveResource(selection);
            if (resource == null)
            {
                StatusText = "请先点击一个资源点，再执行采集";
                return;
            }

            if (resource.Amount <= 0)
            {
                StatusText = "该资源点已经采空";
                return;
            }

            if (!IsNear(character, resource.Row, resource.Col, InteractionRangeCells))
            {
                MoveCharacterNear(character, resource.Row, resource.Col, "正在前往资源点，抵达九宫格后会自动采集；可点击停止打断", new TrialPendingAction("harvest", TrialObjectKind.Resource, resource.Id));
                return;
            }

            int amount = Mathf.Min(HarvestAmount + GetTechLevel(character.TeamId, "Efficiency") * 10, resource.Amount);
            resource.Amount -= amount;
            if (teams.TryGetValue(character.TeamId, out TrialTeam team))
            {
                team.Material += amount;
                team.Score += amount * 2;
                TrialFactory factory = GetTeamFactory(character.TeamId);
                if (factory != null) factory.Source += amount;
            }

            character.State = CharacterState.Harvesting;
            character.PendingAction = null;
            StatusText = $"采集成功：队伍 {character.TeamId} 原料 +{amount}，资源剩余 {resource.Amount}";
        }
        private void Occupy(TrialSelection selection)
        {
            if (!TryGetActiveCharacter(out TrialCharacter character))
            {
                StatusText = "请先创建并选中角色再占领算力中心";
                return;
            }

            TrialComputeCenter center = ResolveComputeCenter(selection);
            if (center == null)
            {
                StatusText = "请先点击一个算力中心，再执行占领";
                return;
            }

            if (character.Type == CharacterType.AutonomousCar)
            {
                StatusText = "THUAI9 规则：无人车不能占领算力中心，请使用无人机或机器人";
                return;
            }

            if (!IsNear(character, center.Row, center.Col, InteractionRangeCells))
            {
                MoveCharacterNear(character, center.Row, center.Col, "正在前往算力中心，抵达九宫格后会自动占领；可点击停止打断", new TrialPendingAction("occupy", TrialObjectKind.ComputeCenter, center.CenterId));
                return;
            }

            center.OwnerTeamId = character.TeamId;
            center.OccupyProgress = 100;
            if (teams.TryGetValue(character.TeamId, out TrialTeam team))
            {
                team.ComputePower += 50;
                team.Score += OccupyScore;
                TrialFactory factory = GetTeamFactory(character.TeamId);
                if (factory != null) factory.ComputingPower += 50;
            }

            character.State = CharacterState.Ocuppying;
            character.PendingAction = null;
            StatusText = $"占领成功：算力中心归属队伍 {character.TeamId}，算力 +50";
        }
        private void Produce(TrialSelection selection, GoodsType goodsType)
        {
            TrialFactory factory = ResolveFactory(selection, requirePlayerTeam: false);
            if (factory == null)
            {
                StatusText = "请先选择队伍工厂，再生产商品";
                return;
            }

            if (!teams.TryGetValue(factory.TeamId, out TrialTeam team))
            {
                StatusText = "试玩：该工厂不属于当前双队伍";
                return;
            }

            int cost = GetGoodsCost(goodsType, factory.TeamId);
            int storage = GetFactoryStorageCapacity(factory.TeamId);
            int current = factory.ProductInventory.Values.Sum();
            if (current >= storage)
            {
                StatusText = $"工厂库存已满：{current}/{storage}";
                return;
            }

            if (team.Material < cost || factory.Source < cost)
            {
                StatusText = $"原料不足：生产 {FormatGoodsName(goodsType)} 需要 {cost} Source，请先采集资源";
                return;
            }

            team.Material -= cost;
            factory.Source = Mathf.Max(0, factory.Source - cost);
            team.Score += GetGoodsBasePrice(goodsType);
            AddInventory(factory.ProductInventory, goodsType, 1);
            StatusText = $"工厂生产完成：{FormatGoodsName(goodsType)} +1，消耗 Source {cost}";
        }
        private void UpgradeTech(TrialSelection selection, TrialTechRule tech)
        {
            TrialFactory factory = ResolveFactory(selection, requirePlayerTeam: false);
            if (factory == null)
            {
                StatusText = "请先选择队伍工厂，再升级科技";
                return;
            }

            if (!teams.TryGetValue(factory.TeamId, out TrialTeam team))
            {
                StatusText = "试玩：该工厂不属于当前双队伍";
                return;
            }

            int currentLevel = GetTechLevel(factory.TeamId, tech.Key);
            if (currentLevel >= TechMaxLevel)
            {
                StatusText = $"{tech.Label} 已满级（Lv.{TechMaxLevel}）";
                return;
            }

            if (team.ComputePower < tech.Cost || factory.ComputingPower < tech.Cost)
            {
                StatusText = $"算力不足：升级 {tech.Label} 需要 {tech.Cost} 算力";
                return;
            }

            team.ComputePower -= tech.Cost;
            factory.ComputingPower = Mathf.Max(0, factory.ComputingPower - tech.Cost);
            team.TechLevels[tech.Key] = currentLevel + 1;
            team.Score += 180;
            string extra = tech.Key == "Robust" ? "（THUAI9 logic 中生命/耐久共用 Robust 等级）" : string.Empty;
            StatusText = $"科技升级：{tech.Label} / {FormatTechName(tech.Key)} Lv.{team.TechLevels[tech.Key]} {extra}";
        }
        private void Attack(TrialSelection selection)
        {
            if (!TryGetActiveCharacter(out TrialCharacter attacker))
            {
                StatusText = "请先创建并选中角色再攻击";
                return;
            }

            TrialCharacter targetCharacter = ResolveEnemyCharacter(selection, attacker.TeamId);
            TrialFactory targetFactory = targetCharacter == null ? ResolveEnemyFactory(selection, attacker.TeamId) : null;
            if (targetCharacter == null && targetFactory == null)
            {
                targetCharacter = FindNearestEnemyCharacter(attacker);
            }

            if (targetCharacter != null)
            {
                if (!IsNear(attacker, targetCharacter.Row, targetCharacter.Col, InteractionRangeCells + 1))
                {
                    MoveCharacterNear(attacker, targetCharacter.Row, targetCharacter.Col, "正在前往敌方角色附近，抵达后会自动攻击；可点击停止打断", new TrialPendingAction("attack-character", TrialObjectKind.Character, targetCharacter.Guid));
                    return;
                }

                int damage = GetCharacterAttack(attacker.Type) + GetTechLevel(attacker.TeamId, "Warrior") * 8;
                targetCharacter.Hp = Mathf.Max(0, targetCharacter.Hp - damage);
                targetCharacter.State = targetCharacter.Hp <= 0 ? CharacterState.Deceased : CharacterState.KnockedBack;
                attacker.State = CharacterState.Attacking;
                attacker.PendingAction = null;
                teams[attacker.TeamId].Score += damage * 2;
                StatusText = targetCharacter.Hp <= 0 ? "攻击反馈：敌方角色已失去行动能力" : $"攻击反馈：造成 {damage} 伤害，目标剩余 {targetCharacter.Hp}";
                return;
            }

            if (targetFactory != null)
            {
                if (!IsNear(attacker, targetFactory.Row, targetFactory.Col, InteractionRangeCells + 1))
                {
                    MoveCharacterNear(attacker, targetFactory.Row, targetFactory.Col, "正在前往敌方工厂附近，抵达后会自动攻击；可点击停止打断", new TrialPendingAction("attack-factory", TrialObjectKind.Factory, targetFactory.FactoryId));
                    return;
                }

                int damage = Mathf.Max(1, GetCharacterAttack(attacker.Type) + GetTechLevel(attacker.TeamId, "Warrior") * 8 - 20);
                targetFactory.Hp = Mathf.Max(0, targetFactory.Hp - damage);
                attacker.State = CharacterState.Attacking;
                attacker.PendingAction = null;
                teams[attacker.TeamId].Score += damage;
                StatusText = targetFactory.Hp <= 0 ? "攻击反馈：敌方工厂已被摧毁" : $"攻击反馈：敌方工厂剩余血量 {targetFactory.Hp}";
                return;
            }

            StatusText = "没有可攻击目标：请点击敌方角色/工厂，或先创建两队角色";
        }
        private void Recover(TrialSelection selection)
        {
            TrialFactory selectedFactory = ResolveFactory(selection, requirePlayerTeam: false);
            if (selectedFactory != null)
            {
                RecoverFactory(selectedFactory);
                return;
            }

            if (!TryGetActiveCharacter(out TrialCharacter character))
            {
                StatusText = "请先创建并选中角色再恢复";
                return;
            }

            TrialFactory factory = GetTeamFactory(character.TeamId);
            if (factory == null)
            {
                StatusText = "未找到该队工厂，无法恢复角色";
                return;
            }

            int maxHp = GetCharacterMaxHp(character.Type) + GetTechLevel(character.TeamId, "Robust") * 30;
            if (character.Hp >= maxHp)
            {
                StatusText = "当前角色生命值已满";
                return;
            }

            if (!IsNear(character, factory.Row, factory.Col, InteractionRangeCells))
            {
                MoveCharacterNear(character, factory.Row, factory.Col, "正在返回己方工厂，抵达后会自动恢复；可点击停止打断", new TrialPendingAction("recover-character", TrialObjectKind.Character, character.Guid));
                return;
            }

            if (!teams.TryGetValue(character.TeamId, out TrialTeam team) || team.ComputePower < RepairComputeCost)
            {
                StatusText = $"算力不足：恢复需要 {RepairComputeCost} 算力";
                return;
            }

            team.ComputePower -= RepairComputeCost;
            factory.ComputingPower = Mathf.Max(0, factory.ComputingPower - RepairComputeCost);
            character.Hp = Mathf.Min(maxHp, character.Hp + RepairAmount);
            character.State = CharacterState.Idle;
            character.PendingAction = null;
            StatusText = $"恢复完成：生命值 {character.Hp}/{maxHp}，消耗算力 {RepairComputeCost}";
        }
        private void StopCurrentAction()
        {
            if (TryGetActiveCharacter(out TrialCharacter character))
            {
                CancelPlannedMove(character);
                character.State = CharacterState.Idle;
                StatusText = "已停止当前动作，移动路径已打断";
            }
            else
            {
                StatusText = "当前没有角色动作；请先选中一个角色";
            }
        }
        private void HandleKeyboardShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            {
                MoveBy(-1, 0);
            }
            else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
            {
                MoveBy(1, 0);
            }
            else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                MoveBy(0, -1);
            }
            else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                MoveBy(0, 1);
            }
        }
        private bool CanExecuteAction(string rawAction, TrialSelection selection)
        {
            return GetVisibleActions(selection).Contains(NormalizeAction(rawAction));
        }

        private List<string> GetVisibleActions(TrialSelection selection)
        {
            List<string> actions = new List<string> { "reset-trial" };
            switch (selection.Kind)
            {
                case TrialObjectKind.Factory:
                    actions.Add("create-drone");
                    actions.Add("create-robot");
                    actions.Add("create-car");
                    AddGoodsActions(actions, "produce");
                    AddTechActions(actions);
                    actions.Add("recover");
                    AddGoodsActions(actions, "load");
                    actions.Add("stop");
                    return actions;
                case TrialObjectKind.Character:
                    actions.Add("stop");
                    actions.Add("harvest");
                    actions.Add("occupy");
                    actions.Add("attack");
                    actions.Add("recover");
                    AddGoodsActions(actions, "load");
                    AddGoodsActions(actions, "buy");
                    AddGoodsActions(actions, "sell");
                    return actions;
                case TrialObjectKind.Resource:
                    actions.Add("move");
                    actions.Add("harvest");
                    actions.Add("stop");
                    return actions;
                case TrialObjectKind.ComputeCenter:
                    actions.Add("move");
                    actions.Add("occupy");
                    actions.Add("stop");
                    return actions;
                case TrialObjectKind.Market:
                    actions.Add("move");
                    AddGoodsActions(actions, "buy");
                    AddGoodsActions(actions, "sell");
                    actions.Add("stop");
                    return actions;
                case TrialObjectKind.Tile:
                    actions.Add("move");
                    actions.Add("stop");
                    return actions;
                default:
                    return actions;
            }
        }

        private static void AddGoodsActions(List<string> actions, string prefix)
        {
            foreach (TrialGoodsRule rule in GoodsRules) actions.Add(prefix + "-" + rule.Token);
        }

        private static void AddTechActions(List<string> actions)
        {
            foreach (TrialTechRule rule in TechRules) actions.Add(rule.Action);
        }
        private TrialSelection BuildSelection(WorldObjectInfo info, Vector2Int? tile)
        {
            if (info != null)
            {
                TrialObjectKind kind = ParseObjectKind(info.objectType);
                return new TrialSelection { Kind = kind, Guid = info.guid, TeamId = info.teamId, PlayerId = info.playerId, Row = info.gridX, Col = info.gridY, HasPosition = true };
            }

            if (tile.HasValue)
            {
                int row = tile.Value.x;
                int col = tile.Value.y;
                if (TryBuildSelectionAtCell(row, col, out TrialSelection objectSelection)) return objectSelection;
                return new TrialSelection { Kind = TrialObjectKind.Tile, Row = row, Col = col, HasPosition = true };
            }

            return TrialSelection.None;
        }

        private bool TryBuildSelectionAtCell(int row, int col, out TrialSelection selection)
        {
            foreach (TrialCharacter character in characters.Values)
            {
                if (character.Row == row && character.Col == col && character.Hp > 0)
                {
                    selection = BuildCharacterSelection(character);
                    return true;
                }
            }
            foreach (TrialFactory factory in factories.Values)
            {
                if (factory.Row == row && factory.Col == col)
                {
                    selection = BuildFactorySelection(factory);
                    return true;
                }
            }
            foreach (TrialResource resource in resources.Values)
            {
                if (resource.Row == row && resource.Col == col)
                {
                    selection = BuildResourceSelection(resource);
                    return true;
                }
            }
            foreach (TrialComputeCenter center in computeCenters.Values)
            {
                if (center.Row == row && center.Col == col)
                {
                    selection = BuildComputeCenterSelection(center);
                    return true;
                }
            }
            foreach (TrialMarket market in markets.Values)
            {
                if (market.Row == row && market.Col == col)
                {
                    selection = BuildMarketSelection(market);
                    return true;
                }
            }
            selection = TrialSelection.None;
            return false;
        }
        private TrialObjectKind ParseObjectKind(string objectType)
        {
            switch (objectType)
            {
                case "Character":
                    return TrialObjectKind.Character;
                case "Factory":
                    return TrialObjectKind.Factory;
                case "Resource":
                    return TrialObjectKind.Resource;
                case "ComputeCenter":
                    return TrialObjectKind.ComputeCenter;
                case "Market":
                    return TrialObjectKind.Market;
                default:
                    return TrialObjectKind.Tile;
            }
        }

        private static string NormalizeAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action)) return string.Empty;
            string normalized = action.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "create-drone":
                case "create_robot_drone":
                case "drone":
                    return "create-drone";
                case "create-robot":
                case "create_robot":
                case "robot":
                    return "create-robot";
                case "create-car":
                case "create-autonomous-car":
                case "create_autonomous_car":
                case "autonomous-car":
                case "car":
                    return "create-car";
                case "create-character":
                case "create_character":
                case "createcharacter":
                    return "create";
                case "uplevel-tech":
                case "upgrade-tech":
                case "uplevel":
                case "tech":
                    return "upgrade";
                case "end-all-action":
                case "end":
                case "idle":
                    return "stop";
                case "restart":
                case "reset":
                case "resettrial":
                    return "reset-trial";
                default:
                    return normalized.Replace('_', '-');
            }
        }
        private void SubmitFrame()
        {
            MessageToClient frame = BuildFrame();
            int frameIndex = Mathf.Max(0, Mathf.RoundToInt(elapsedMs / FrameDurationMs));
            int elapsed = Mathf.Max(0, Mathf.RoundToInt(elapsedMs));
            FrameSourceHub.SubmitImmediate(frame, frameIndex, elapsed, StatusText);
        }

        private MessageToClient BuildFrame()
        {
            MessageToClient frame = new MessageToClient
            {
                GameState = GameState.GameRunning,
                AllMessage = BuildAllMessage()
            };
            frame.ObjMessage.Add(new MessageOfObj { MapMessage = BuildMapMessage() });

            foreach (TrialFactory factory in factories.Values.OrderBy(f => f.FactoryId))
            {
                frame.ObjMessage.Add(new MessageOfObj { FactoryMessage = BuildFactoryMessage(factory) });
            }

            foreach (TrialResource resource in resources.Values.OrderBy(r => r.Id))
            {
                frame.ObjMessage.Add(new MessageOfObj { ResourceMessage = BuildResourceMessage(resource) });
            }

            foreach (TrialComputeCenter center in computeCenters.Values.OrderBy(c => c.CenterId))
            {
                Vector2Int gamePosition = GridToGameCell(center.Row, center.Col);
                frame.ObjMessage.Add(new MessageOfObj
                {
                    ComputeCenterMessage = new MessageOfComputeCenter
                    {
                        CenterId = center.CenterId,
                        X = gamePosition.x,
                        Y = gamePosition.y,
                        OwnerTeamId = center.OwnerTeamId,
                        OccupyProgress = center.OccupyProgress
                    }
                });
            }

            foreach (TrialMarket market in markets.Values.OrderBy(m => m.MarketId))
            {
                frame.ObjMessage.Add(new MessageOfObj { MarketMessage = BuildMarketMessage(market) });
            }

            foreach (TrialCharacter character in characters.Values.OrderBy(c => c.Guid))
            {
                frame.ObjMessage.Add(new MessageOfObj { CharacterMessage = BuildCharacterMessage(character) });
            }

            foreach (TrialTeam team in teams.Values.OrderBy(t => t.TeamId))
            {
                frame.ObjMessage.Add(new MessageOfObj { TeamMessage = BuildTeamMessage(team) });
            }

            return frame;
        }

        private MessageOfMap BuildMapMessage()
        {
            MessageOfMap map = new MessageOfMap
            {
                Width = MapCols,
                Height = MapRows
            };

            for (int row = 0; row < MapRows; row++)
            {
                MessageOfMap.Types.Row mapRow = new MessageOfMap.Types.Row();
                for (int col = 0; col < MapCols; col++)
                {
                    PlaceType place = GetPlaceType(row, col);
                    if (place == PlaceType.Factory && !activeFactoryCells.Contains(CellKey(row, col)))
                    {
                        place = PlaceType.Space;
                    }
                    mapRow.Cols.Add(place);
                }
                map.Rows.Add(mapRow);
            }

            return map;
        }

        private MessageOfAll BuildAllMessage()
        {
            MessageOfAll all = new MessageOfAll
            {
                GameTime = Mathf.RoundToInt(elapsedMs)
            };

            for (int teamId = 1; teamId <= TrialTeamCount; teamId++)
            {
                TrialTeam team = teams[teamId];
                MessageOfAll.Types.TeamInfo info = new MessageOfAll.Types.TeamInfo
                {
                    Score = Mathf.Clamp((int)team.Score, 0, int.MaxValue),
                    Material = team.Material,
                    ComputePower = team.ComputePower,
                    FactoryHp = GetTeamFactory(teamId)?.Hp ?? 0
                };

                foreach (KeyValuePair<string, int> pair in team.TechLevels)
                {
                    info.TechLevels.Add(pair.Key, pair.Value);
                }

                all.Teams.Add(info);
            }

            return all;
        }

        private static MessageOfTeam BuildTeamMessage(TrialTeam team)
        {
            return new MessageOfTeam
            {
                TeamId = team.TeamId,
                Score = Mathf.Clamp((int)team.Score, 0, int.MaxValue),
                Material = team.Material,
                ComputePower = team.ComputePower
            };
        }

        private MessageOfFactory BuildFactoryMessage(TrialFactory factory)
        {
            Vector2Int gamePosition = GridToGameCell(factory.Row, factory.Col);
            MessageOfFactory message = new MessageOfFactory
            {
                FactoryId = factory.FactoryId,
                TeamId = factory.TeamId,
                X = gamePosition.x,
                Y = gamePosition.y,
                Hp = factory.Hp,
                Robust = 1,
                Storage = GetFactoryStorageCapacity(factory.TeamId),
                Efficiency = 100 + GetTechLevel(factory.TeamId, "Production") * 15,
                Source = factory.Source,
                ComputingPower = factory.ComputingPower,
                CanProduce = true,
                CanRecruit = true
            };

            foreach (KeyValuePair<GoodsType, int> pair in factory.ProductInventory)
            {
                message.ProductInventory.Add(new MessageOfFactory.Types.GoodsStack
                {
                    ProductType = pair.Key,
                    Quantity = pair.Value
                });
            }

            return message;
        }

        private static MessageOfResource BuildResourceMessage(TrialResource resource)
        {
            Vector2Int gamePosition = GridToGameCell(resource.Row, resource.Col);
            return new MessageOfResource
            {
                Id = Mathf.Clamp((int)resource.Id, 0, int.MaxValue),
                X = gamePosition.x,
                Y = gamePosition.y,
                ResourceType = ResourceType.LargeResource,
                RemainingAmount = resource.Amount,
                MaxAmount = ResourceMaxAmount,
                ResourceState = resource.Amount > 0 ? ResourceState.Harvestable : ResourceState.Harvested
            };
        }

        private MessageOfMarket BuildMarketMessage(TrialMarket market)
        {
            Vector2Int gamePosition = GridToGameCell(market.Row, market.Col);
            MessageOfMarket message = new MessageOfMarket
            {
                MarketId = market.MarketId,
                X = gamePosition.x,
                Y = gamePosition.y,
                MarketType = market.MarketType
            };

            foreach (GoodsType goodsType in GoodsOrder)
            {
                message.PriceList.Add(BuildPriceEntry(goodsType, GetMarketPrice(market, goodsType, 0), GetInventory(market.TradedQuantities, goodsType)));
            }
            return message;
        }
        private static MessageOfMarket.Types.PriceEntry BuildPriceEntry(GoodsType goodsType, int price, int tradedQuantity)
        {
            return new MessageOfMarket.Types.PriceEntry
            {
                GoodsType = goodsType,
                Price = price,
                TradedQuantity = tradedQuantity
            };
        }
        private MessageOfCharacter BuildCharacterMessage(TrialCharacter character)
        {
            Vector2 gamePosition = Tool.GridToGame(character.Row, character.Col);
            int gameX = Mathf.RoundToInt(gamePosition.x);
            int gameY = Mathf.RoundToInt(gamePosition.y);
            return new MessageOfCharacter
            {
                Guid = character.Guid,
                TeamId = character.TeamId,
                PlayerId = character.PlayerId,
                X = gameX,
                Y = gameY,
                FacingDirection = 0,
                CharacterType = character.Type,
                CharacterActiveState = character.State,
                Hp = character.Hp,
                CommonAttack = GetCharacterAttack(character.Type) + GetTechLevel(character.TeamId, "Warrior") * 8,
                CommonAttackCd = character.State == CharacterState.Attacking ? 1500 : 1000,
                CommonAttackRange = CharacterAttackRange + GetTechLevel(character.TeamId, "AttackSize") * 500,
                Speed = CharacterSpeed + GetTechLevel(character.TeamId, "MoveSpeed") * 200,
                CurrentLoad = GetCurrentLoad(character),
                CarryCapacity = GetCharacterCarryCapacity(character),
                ViewRange = GetCharacterViewRange(character.Type),
                HarvestRatePerSec = 20 + GetTechLevel(character.TeamId, "Efficiency") * 5
            };
        }
        private TrialResource ResolveResource(TrialSelection selection)
        {
            if (selection.Kind == TrialObjectKind.Resource && resources.TryGetValue(selection.Guid, out TrialResource selected)) return selected;
            return null;
        }

        private TrialComputeCenter ResolveComputeCenter(TrialSelection selection)
        {
            if (selection.Kind == TrialObjectKind.ComputeCenter && computeCenters.TryGetValue(selection.Guid, out TrialComputeCenter selected)) return selected;
            return null;
        }

        private TrialMarket ResolveMarket(TrialSelection selection)
        {
            if (selection.Kind == TrialObjectKind.Market && markets.TryGetValue(selection.Guid, out TrialMarket selected)) return selected;
            return null;
        }
        private TrialFactory ResolveFactory(TrialSelection selection, bool requirePlayerTeam)
        {
            if (selection.Kind == TrialObjectKind.Factory && factories.TryGetValue(selection.Guid, out TrialFactory factory))
            {
                if (!requirePlayerTeam || factory.TeamId == playerTeamId)
                {
                    return factory;
                }
            }

            return null;
        }

        private TrialCharacter ResolveEnemyCharacter(TrialSelection selection, long attackerTeamId)
        {
            if (selection.Kind == TrialObjectKind.Character && characters.TryGetValue(selection.Guid, out TrialCharacter character) && character.TeamId != attackerTeamId && character.Hp > 0)
            {
                return character;
            }

            return null;
        }

        private TrialFactory ResolveEnemyFactory(TrialSelection selection, long attackerTeamId)
        {
            if (selection.Kind == TrialObjectKind.Factory && factories.TryGetValue(selection.Guid, out TrialFactory factory) && factory.TeamId != attackerTeamId && factory.Hp > 0)
            {
                return factory;
            }

            return null;
        }

        private TrialFactory GetPlayerFactory()
        {
            return GetTeamFactory(playerTeamId);
        }

        private TrialFactory GetTeamFactory(long teamId)
        {
            return factories.Values.FirstOrDefault(factory => factory.TeamId == teamId);
        }

        private bool CanCreateCharacter(TrialSelection selection)
        {
            TrialFactory factory = ResolveFactory(selection, requirePlayerTeam: false);
            if (factory == null || !teams.TryGetValue(factory.TeamId, out TrialTeam team))
            {
                return false;
            }

            int existingCount = characters.Values.Count(c => c.TeamId == factory.TeamId && c.Hp > 0);
            return existingCount < MaxCharactersPerTeam && team.ComputePower >= CharacterCreateCost;
        }

        private long NextAvailablePlayerId(long teamId)
        {
            for (long candidate = 1; candidate <= MaxCharactersPerTeam; candidate++)
            {
                if (characters.Values.All(c => c.TeamId != teamId || c.PlayerId != candidate || c.Hp <= 0))
                {
                    return candidate;
                }
            }

            return 0;
        }

        private bool TryGetActiveCharacter(out TrialCharacter character)
        {
            if (activeCharacterGuid != 0 && characters.TryGetValue(activeCharacterGuid, out character) && character.Hp > 0)
            {
                return true;
            }

            character = null;
            return false;
        }

        private bool TryGetSelectedCharacter(out TrialCharacter character)
        {
            if (currentSelection.Kind == TrialObjectKind.Character
                && characters.TryGetValue(currentSelection.Guid, out character)
                && character.Hp > 0)
            {
                activeCharacterGuid = character.Guid;
                return true;
            }

            character = null;
            return false;
        }

        private void LoadGoods(TrialSelection selection, GoodsType goodsType)
        {
            if (!TryGetActiveCharacter(out TrialCharacter character))
            {
                StatusText = "请先创建并选中角色，再装载商品";
                return;
            }
            TrialFactory factory = ResolveFactory(selection, requirePlayerTeam: false) ?? GetTeamFactory(character.TeamId);
            if (factory == null || factory.TeamId != character.TeamId)
            {
                StatusText = "装载只能在该角色所属队伍的工厂进行";
                return;
            }
            if (!IsNear(character, factory.Row, factory.Col, InteractionRangeCells))
            {
                MoveCharacterNear(character, factory.Row, factory.Col, "正在前往己方工厂，抵达后会自动装载；可点击停止打断", new TrialPendingAction("load", TrialObjectKind.Factory, factory.FactoryId, goodsType));
                return;
            }
            int available = GetInventory(factory.ProductInventory, goodsType);
            if (available <= 0)
            {
                StatusText = $"工厂没有可装载的 {FormatGoodsName(goodsType)}，请先生产";
                return;
            }
            int capacity = GetCharacterCarryCapacity(character);
            int currentLoad = GetCurrentLoad(character);
            if (currentLoad >= capacity)
            {
                StatusText = $"角色负载已满：{currentLoad}/{capacity}";
                return;
            }
            int amount = Mathf.Min(GoodsTransferAmount, Mathf.Min(available, capacity - currentLoad));
            AddInventory(factory.ProductInventory, goodsType, -amount);
            AddInventory(character.GoodsLoad, goodsType, amount);
            character.State = CharacterState.Trading;
            character.PendingAction = null;
            StatusText = $"装载成功：{FormatGoodsName(goodsType)} +{amount}，负载 {GetCurrentLoad(character)}/{capacity}";
        }

        private void TradeGoods(TrialSelection selection, GoodsType goodsType, bool buy)
        {
            if (!TryGetActiveCharacter(out TrialCharacter character))
            {
                StatusText = "请先创建并选中角色，再进行市场交易";
                return;
            }
            TrialMarket market = ResolveMarket(selection);
            if (market == null)
            {
                StatusText = "请先点击一个市场，再进行买入/卖出";
                return;
            }
            if (!IsNear(character, market.Row, market.Col, InteractionRangeCells))
            {
                MoveCharacterNear(character, market.Row, market.Col, buy ? "正在前往市场，抵达后会自动买入；可点击停止打断" : "正在前往市场，抵达后会自动卖出；可点击停止打断", new TrialPendingAction(buy ? "buy" : "sell", TrialObjectKind.Market, market.MarketId, goodsType));
                return;
            }
            if (!teams.TryGetValue(character.TeamId, out TrialTeam team))
            {
                StatusText = "试玩：该角色不属于当前双队伍";
                return;
            }
            int price = GetMarketPrice(market, goodsType, character.TeamId);
            if (buy)
            {
                int capacity = GetCharacterCarryCapacity(character);
                int currentLoad = GetCurrentLoad(character);
                if (currentLoad >= capacity)
                {
                    StatusText = $"角色负载已满：{currentLoad}/{capacity}";
                    return;
                }
                if (team.Score < price)
                {
                    StatusText = $"得分不足：买入 {FormatGoodsName(goodsType)} 需要 {price} 分";
                    return;
                }
                team.Score -= price;
                AddInventory(character.GoodsLoad, goodsType, GoodsTransferAmount);
                AddInventory(market.TradedQuantities, goodsType, GoodsTransferAmount);
                character.State = CharacterState.Trading;
                StatusText = $"买入成功：{FormatGoodsName(goodsType)} +{GoodsTransferAmount}，花费 {price} 分";
                return;
            }
            int have = GetInventory(character.GoodsLoad, goodsType);
            if (have <= 0)
            {
                StatusText = $"角色没有 {FormatGoodsName(goodsType)} 可卖出；请先在工厂装载或市场买入";
                return;
            }
            int amount = Mathf.Min(GoodsTransferAmount, have);
            int income = price * amount;
            int priceTech = GetTechLevel(character.TeamId, "Price");
            if (priceTech > 0) income = Mathf.RoundToInt(income * (1f + priceTech * 0.1f));
            AddInventory(character.GoodsLoad, goodsType, -amount);
            AddInventory(market.TradedQuantities, goodsType, amount);
            team.Score += income;
            character.State = CharacterState.Trading;
            StatusText = $"卖出成功：{FormatGoodsName(goodsType)} -{amount}，得分 +{income}";
        }

        private void RecoverFactory(TrialFactory factory)
        {
            if (!teams.TryGetValue(factory.TeamId, out TrialTeam team))
            {
                StatusText = "试玩：该工厂不属于当前双队伍";
                return;
            }
            if (factory.Hp >= FactoryHp)
            {
                StatusText = "当前工厂血量已满";
                return;
            }
            if (team.ComputePower < RepairComputeCost || factory.ComputingPower < RepairComputeCost)
            {
                StatusText = $"算力不足：修复工厂需要 {RepairComputeCost} 算力";
                return;
            }
            team.ComputePower -= RepairComputeCost;
            factory.ComputingPower = Mathf.Max(0, factory.ComputingPower - RepairComputeCost);
            factory.Hp = Mathf.Min(FactoryHp, factory.Hp + RepairAmount);
            StatusText = $"工厂修复完成：血量 {factory.Hp}/{FactoryHp}";
        }

        private TrialSelection BuildSelectionFromGuid(TrialObjectKind kind, long guid)
        {
            switch (kind)
            {
                case TrialObjectKind.Character:
                    return characters.TryGetValue(guid, out TrialCharacter character) ? BuildCharacterSelection(character) : TrialSelection.None;
                case TrialObjectKind.Factory:
                    return factories.TryGetValue(guid, out TrialFactory factory) ? BuildFactorySelection(factory) : TrialSelection.None;
                case TrialObjectKind.Resource:
                    return resources.TryGetValue(guid, out TrialResource resource) ? BuildResourceSelection(resource) : TrialSelection.None;
                case TrialObjectKind.ComputeCenter:
                    return computeCenters.TryGetValue(guid, out TrialComputeCenter center) ? BuildComputeCenterSelection(center) : TrialSelection.None;
                case TrialObjectKind.Market:
                    return markets.TryGetValue(guid, out TrialMarket market) ? BuildMarketSelection(market) : TrialSelection.None;
                default:
                    return TrialSelection.None;
            }
        }

        private static TrialSelection BuildCharacterSelection(TrialCharacter character)
        {
            return new TrialSelection { Kind = TrialObjectKind.Character, Guid = character.Guid, TeamId = character.TeamId, PlayerId = character.PlayerId, Row = character.Row, Col = character.Col, HasPosition = true };
        }
        private static TrialSelection BuildFactorySelection(TrialFactory factory)
        {
            return new TrialSelection { Kind = TrialObjectKind.Factory, Guid = factory.FactoryId, TeamId = factory.TeamId, Row = factory.Row, Col = factory.Col, HasPosition = true };
        }
        private static TrialSelection BuildResourceSelection(TrialResource resource)
        {
            return new TrialSelection { Kind = TrialObjectKind.Resource, Guid = resource.Id, Row = resource.Row, Col = resource.Col, HasPosition = true };
        }
        private static TrialSelection BuildComputeCenterSelection(TrialComputeCenter center)
        {
            return new TrialSelection { Kind = TrialObjectKind.ComputeCenter, Guid = center.CenterId, Row = center.Row, Col = center.Col, HasPosition = true };
        }
        private static TrialSelection BuildMarketSelection(TrialMarket market)
        {
            return new TrialSelection { Kind = TrialObjectKind.Market, Guid = market.MarketId, Row = market.Row, Col = market.Col, HasPosition = true };
        }
        private TrialResource FindNearestResource(TrialCharacter character)
        {
            TrialResource best = null;
            int bestDistance = int.MaxValue;
            foreach (TrialResource resource in resources.Values)
            {
                if (resource.Amount <= 0)
                {
                    continue;
                }

                int distance = Manhattan(character.Row, character.Col, resource.Row, resource.Col);
                if (distance < bestDistance)
                {
                    best = resource;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private TrialComputeCenter FindNearestComputeCenter(TrialCharacter character)
        {
            TrialComputeCenter best = null;
            int bestDistance = int.MaxValue;
            foreach (TrialComputeCenter center in computeCenters.Values)
            {
                int distance = Manhattan(character.Row, character.Col, center.Row, center.Col);
                if (distance < bestDistance)
                {
                    best = center;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private TrialCharacter FindNearestEnemyCharacter(TrialCharacter attacker)
        {
            TrialCharacter best = null;
            int bestDistance = int.MaxValue;
            foreach (TrialCharacter character in characters.Values)
            {
                if (character.TeamId == attacker.TeamId || character.Hp <= 0)
                {
                    continue;
                }

                int distance = Manhattan(attacker.Row, attacker.Col, character.Row, character.Col);
                if (distance < bestDistance)
                {
                    best = character;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private bool MoveCharacterNear(TrialCharacter character, int targetRow, int targetCol, string status, TrialPendingAction pendingAction)
        {
            List<Vector2Int> targets = FindInteractionTargets(character, targetRow, targetCol, InteractionRangeCells);
            if (targets.Count == 0)
            {
                StatusText = "目标附近没有可站立地格，无法靠近交互";
                return false;
            }
            List<Vector2Int> path = FindPath(character, targets);
            if (path == null)
            {
                StatusText = "无法寻路到目标附近，请选择其他角色或目标";
                return false;
            }
            BeginPath(character, path, pendingAction, status);
            return true;
        }

        private void BeginPath(TrialCharacter character, List<Vector2Int> path, TrialPendingAction pendingAction, string status)
        {
            CancelPlannedMove(character);
            foreach (Vector2Int step in path) character.Path.Enqueue(step);
            character.PendingAction = pendingAction;
            character.MoveStepTimerMs = 0f;
            if (character.Path.Count == 0)
            {
                character.State = CharacterState.Idle;
                if (pendingAction != null)
                {
                    ExecutePendingAction(character, pendingAction);
                    return;
                }
            }
            else
            {
                character.State = CharacterState.Moving;
            }
            StatusText = status;
        }

        private void CancelPlannedMove(TrialCharacter character)
        {
            character.Path.Clear();
            character.PendingAction = null;
            character.MoveStepTimerMs = 0f;
        }

        private void AdvanceCharacterPaths(float deltaMs)
        {
            foreach (TrialCharacter character in characters.Values.ToList())
            {
                if (character.Hp <= 0 || character.Path.Count == 0) continue;
                float stepInterval = Mathf.Max(70f, MoveStepIntervalMs - GetTechLevel(character.TeamId, "MoveSpeed") * 18f);
                character.MoveStepTimerMs += deltaMs;
                if (character.MoveStepTimerMs < stepInterval) continue;
                character.MoveStepTimerMs -= stepInterval;
                Vector2Int next = character.Path.Dequeue();
                if (!IsCellAvailableForCharacter(character, next.x, next.y))
                {
                    CancelPlannedMove(character);
                    character.State = CharacterState.Idle;
                    StatusText = $"移动被阻挡：({next.x}, {next.y}) 已不可通行";
                    continue;
                }
                character.Row = next.x;
                character.Col = next.y;
                character.State = CharacterState.Moving;
                if (character.Path.Count == 0)
                {
                    TrialPendingAction pending = character.PendingAction;
                    character.PendingAction = null;
                    character.State = CharacterState.Idle;
                    if (pending != null) ExecutePendingAction(character, pending);
                    else StatusText = $"已抵达 ({character.Row}, {character.Col})";
                }
            }
        }

        private void ExecutePendingAction(TrialCharacter character, TrialPendingAction pending)
        {
            TrialSelection selection = BuildSelectionFromGuid(pending.Kind, pending.TargetGuid);
            switch (pending.Action)
            {
                case "harvest": Harvest(selection); break;
                case "occupy": Occupy(selection); break;
                case "attack-character":
                case "attack-factory": Attack(selection); break;
                case "recover-character": Recover(BuildCharacterSelection(character)); break;
                case "load": LoadGoods(selection, pending.GoodsType); break;
                case "buy": TradeGoods(selection, pending.GoodsType, buy: true); break;
                case "sell": TradeGoods(selection, pending.GoodsType, buy: false); break;
                default: StatusText = "已抵达目标附近"; break;
            }
        }

        private List<Vector2Int> FindInteractionTargets(TrialCharacter character, int targetRow, int targetCol, int range)
        {
            List<Vector2Int> targets = new List<Vector2Int>();
            for (int row = targetRow - range; row <= targetRow + range; row++)
            {
                for (int col = targetCol - range; col <= targetCol + range; col++)
                {
                    if (row == targetRow && col == targetCol) continue;
                    if (row < 0 || col < 0 || row >= MapRows || col >= MapCols) continue;
                    if (IsCellAvailableForCharacter(character, row, col)) targets.Add(new Vector2Int(row, col));
                }
            }
            targets.Sort((left, right) => Manhattan(character.Row, character.Col, left.x, left.y).CompareTo(Manhattan(character.Row, character.Col, right.x, right.y)));
            return targets;
        }

        private List<Vector2Int> FindPath(TrialCharacter character, List<Vector2Int> targets)
        {
            HashSet<long> targetKeys = new HashSet<long>();
            foreach (Vector2Int target in targets)
            {
                if (IsCellAvailableForCharacter(character, target.x, target.y)) targetKeys.Add(CellKey(target.x, target.y));
            }
            if (targetKeys.Count == 0) return null;
            long startKey = CellKey(character.Row, character.Col);
            if (targetKeys.Contains(startKey)) return new List<Vector2Int>();
            bool[,] visited = new bool[MapRows, MapCols];
            Vector2Int[,] previous = new Vector2Int[MapRows, MapCols];
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(new Vector2Int(character.Row, character.Col));
            visited[character.Row, character.Col] = true;
            int[] dRows = { -1, 1, 0, 0 };
            int[] dCols = { 0, 0, -1, 1 };
            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                for (int i = 0; i < dRows.Length; i++)
                {
                    int row = current.x + dRows[i];
                    int col = current.y + dCols[i];
                    if (row < 0 || col < 0 || row >= MapRows || col >= MapCols || visited[row, col]) continue;
                    if (!IsCellAvailableForCharacter(character, row, col)) continue;
                    visited[row, col] = true;
                    previous[row, col] = current;
                    if (targetKeys.Contains(CellKey(row, col))) return ReconstructPath(new Vector2Int(character.Row, character.Col), new Vector2Int(row, col), previous);
                    queue.Enqueue(new Vector2Int(row, col));
                }
            }
            return null;
        }

        private static List<Vector2Int> ReconstructPath(Vector2Int start, Vector2Int end, Vector2Int[,] previous)
        {
            List<Vector2Int> path = new List<Vector2Int>();
            Vector2Int current = end;
            while (current != start)
            {
                path.Add(current);
                current = previous[current.x, current.y];
            }
            path.Reverse();
            return path;
        }
        private Vector2Int FindSpawnCellNearFactory(TrialFactory factory)
        {
            int rowStep = factory.Row < MapRows / 2 ? 1 : -1;
            int colStep = factory.Col < MapCols / 2 ? 1 : -1;
            for (int radius = 2; radius <= 6; radius++)
            {
                Vector2Int[] candidates =
                {
                    new Vector2Int(factory.Row + rowStep * radius, factory.Col + colStep * radius),
                    new Vector2Int(factory.Row + rowStep * radius, factory.Col),
                    new Vector2Int(factory.Row, factory.Col + colStep * radius),
                    new Vector2Int(factory.Row + rowStep * radius, factory.Col - colStep * radius),
                    new Vector2Int(factory.Row - rowStep * radius, factory.Col + colStep * radius)
                };

                foreach (Vector2Int candidate in candidates)
                {
                    if (IsCellAvailable(candidate.x, candidate.y))
                    {
                        return candidate;
                    }
                }
            }

            return FindNearestFreeCell(factory.Row, factory.Col, rowStep);
        }

        private Vector2Int FindNearestFreeCell(int row, int col, int preferredDirection)
        {
            int direction = preferredDirection == 0 ? 1 : Math.Sign(preferredDirection);
            for (int radius = 1; radius <= 8; radius++)
            {
                Vector2Int[] offsets =
                {
                    new Vector2Int(0, direction * radius),
                    new Vector2Int(direction * radius, 0),
                    new Vector2Int(0, -direction * radius),
                    new Vector2Int(-direction * radius, 0),
                    new Vector2Int(radius, radius),
                    new Vector2Int(-radius, -radius),
                    new Vector2Int(radius, -radius),
                    new Vector2Int(-radius, radius)
                };
                foreach (Vector2Int offset in offsets)
                {
                    int candidateRow = Mathf.Clamp(row + offset.x, 0, MapRows - 1);
                    int candidateCol = Mathf.Clamp(col + offset.y, 0, MapCols - 1);
                    if (IsCellAvailable(candidateRow, candidateCol)) return new Vector2Int(candidateRow, candidateCol);
                }
            }
            return new Vector2Int(Mathf.Clamp(row, 0, MapRows - 1), Mathf.Clamp(col, 0, MapCols - 1));
        }

        private bool IsNear(TrialCharacter character, int row, int col, int range)
        {
            return Mathf.Max(Mathf.Abs(character.Row - row), Mathf.Abs(character.Col - col)) <= range;
        }

        private static int Manhattan(int rowA, int colA, int rowB, int colB)
        {
            return Mathf.Abs(rowA - rowB) + Mathf.Abs(colA - colB);
        }

        private bool IsBlocked(int row, int col)
        {
            if (row < 0 || col < 0 || row >= MapRows || col >= MapCols) return true;
            PlaceType place = GetPlaceType(row, col);
            return place != PlaceType.Space && place != PlaceType.Bush;
        }

        private bool IsCellAvailable(int row, int col)
        {
            return !IsBlocked(row, col) && !characters.Values.Any(c => c.Hp > 0 && c.Row == row && c.Col == col);
        }

        private bool IsCellAvailableForCharacter(TrialCharacter moving, int row, int col)
        {
            return !IsBlocked(row, col) && !characters.Values.Any(c => c.Guid != moving.Guid && c.Hp > 0 && c.Row == row && c.Col == col);
        }
        private static PlaceType GetPlaceType(int row, int col)
        {
            if (row < 0 || col < 0 || row >= MapRows || col >= MapCols)
            {
                return PlaceType.Barrier;
            }

            return (PlaceType)OfficialMap[row][col];
        }

        private int GetTechLevel(long teamId, string tech)
        {
            if (!teams.TryGetValue(teamId, out TrialTeam team))
            {
                return 0;
            }

            return team.TechLevels.TryGetValue(tech, out int level) ? level : 0;
        }

        private long NextGuid()
        {
            return nextGuid++;
        }

        private static long CellKey(int row, int col)
        {
            return ((long)row << 32) | (uint)col;
        }

        private static Vector2Int GridToGameCell(int row, int col)
        {
            Vector2 gamePosition = Tool.GridToGame(row, col);
            return new Vector2Int(Mathf.RoundToInt(gamePosition.x), Mathf.RoundToInt(gamePosition.y));
        }

        private static int GetCharacterMaxHp(CharacterType type)
        {
            switch (type)
            {
                case CharacterType.Drone:
                    return DroneHp;
                case CharacterType.AutonomousCar:
                    return CarHp;
                default:
                    return CharacterHp;
            }
        }

        private static int GetCharacterAttack(CharacterType type)
        {
            switch (type)
            {
                case CharacterType.Drone:
                    return 40;
                case CharacterType.AutonomousCar:
                    return 18;
                default:
                    return 30;
            }
        }

        private static int GetCharacterCarry(CharacterType type)
        {
            return 5;
        }

        private static int GetCharacterViewRange(CharacterType type)
        {
            return type == CharacterType.Drone ? 7000 : 5000;
        }

        private static MarketType GetDefaultMarketType(int row, int col)
        {
            int variant = Mathf.Abs(row * 31 + col * 17) % 3;
            switch (variant)
            {
                case 0:
                    return MarketType.SmallMarket;
                case 1:
                    return MarketType.MediumMarket;
                default:
                    return MarketType.LargeMarket;
            }
        }

        private static string FormatPlaceType(PlaceType place)
        {
            switch (place)
            {
                case PlaceType.Factory:
                    return "工厂";
                case PlaceType.Resource:
                    return "资源点";
                case PlaceType.ComputeCenter:
                    return "算力中心";
                case PlaceType.Market:
                    return "市场";
                case PlaceType.Barrier:
                    return "障碍";
                case PlaceType.Bush:
                    return "草丛";
                default:
                    return "空地";
            }
        }

        private static string FormatCharacterState(CharacterState state)
        {
            switch (state)
            {
                case CharacterState.Harvesting:
                    return "采集中";
                case CharacterState.Attacking:
                    return "攻击中";
                case CharacterState.Ocuppying:
                    return "占领中";
                case CharacterState.Moving:
                    return "移动中";
                case CharacterState.KnockedBack:
                    return "受击";
                case CharacterState.Deceased:
                    return "失去行动能力";
                default:
                    return "空闲";
            }
        }

        private static string FormatCharacterType(CharacterType type)
        {
            switch (type)
            {
                case CharacterType.Drone:
                    return "无人机";
                case CharacterType.Robot:
                    return "机器人";
                case CharacterType.AutonomousCar:
                    return "无人车";
                default:
                    return "未知角色";
            }
        }

        private static string FormatMarketType(MarketType type)
        {
            switch (type)
            {
                case MarketType.SmallMarket:
                    return "小型市场";
                case MarketType.MediumMarket:
                    return "中型市场";
                case MarketType.LargeMarket:
                    return "大型市场";
                default:
                    return "未知市场";
            }
        }

        private static string FormatTechName(string tech)
        {
            switch (tech)
            {
                case "Robust": return "生命/耐久";
                case "Warrior": return "攻击能力";
                case "AttackSize": return "攻击范围";
                case "MoveSpeed": return "移动速度";
                case "Carry": return "携带容量";
                case "Efficiency": return "采集/占领效率";
                case "Production": return "生产效率";
                case "Storage": return "工厂仓储";
                case "Price": return "出售价格";
                case "Cost": return "降低成本";
                default: return tech;
            }
        }

        private static string FormatInventory(TrialFactory factory)
        {
            return FormatInventory(factory.ProductInventory);
        }

        private static string FormatInventory(Dictionary<GoodsType, int> inventory)
        {
            if (inventory == null || inventory.Count == 0 || inventory.Values.All(v => v <= 0)) return "无";
            return string.Join("/", inventory.Where(pair => pair.Value > 0).Select(pair => FormatGoodsName(pair.Key) + "×" + pair.Value));
        }

        private static string FormatGoodsName(GoodsType goodsType)
        {
            foreach (TrialGoodsRule rule in GoodsRules)
            {
                if (rule.GoodsType == goodsType) return rule.Label;
            }
            return goodsType.ToString();
        }

        private string FormatMarketPrices(TrialMarket market)
        {
            return string.Join(" / ", GoodsOrder.Select(goods => FormatGoodsName(goods) + " " + GetMarketPrice(market, goods, 0)));
        }

        private static int GetGoodsBaseCost(GoodsType goodsType)
        {
            foreach (TrialGoodsRule rule in GoodsRules)
            {
                if (rule.GoodsType == goodsType) return rule.Cost;
            }
            return 1;
        }

        private static int GetGoodsBasePrice(GoodsType goodsType)
        {
            foreach (TrialGoodsRule rule in GoodsRules)
            {
                if (rule.GoodsType == goodsType) return rule.Price;
            }
            return 1;
        }

        private int GetGoodsCost(GoodsType goodsType, long teamId)
        {
            return Mathf.Max(1, GetGoodsBaseCost(goodsType) - GetTechLevel(teamId, "Cost") * 2);
        }

        private int GetMarketPrice(TrialMarket market, GoodsType goodsType, long teamId)
        {
            float multiplier;
            switch (market.MarketType)
            {
                case MarketType.SmallMarket: multiplier = 1.1f; break;
                case MarketType.LargeMarket: multiplier = 1.5f; break;
                default: multiplier = 1.3f; break;
            }
            int traded = GetInventory(market.TradedQuantities, goodsType);
            float decay = Mathf.Max(0.5f, 1f - (traded / 50) * 0.05f);
            return Mathf.Max(1, Mathf.RoundToInt(GetGoodsBasePrice(goodsType) * multiplier * decay));
        }

        private int GetFactoryStorageCapacity(long teamId)
        {
            return FactoryBaseStorage + GetTechLevel(teamId, "Storage") * 50;
        }

        private int GetCharacterCarryCapacity(TrialCharacter character)
        {
            return GetCharacterCarry(character.Type) + GetTechLevel(character.TeamId, "Carry") * 10;
        }

        private static int GetCurrentLoad(TrialCharacter character)
        {
            return character.GoodsLoad.Values.Sum();
        }

        private static int GetInventory(Dictionary<GoodsType, int> inventory, GoodsType goodsType)
        {
            return inventory != null && inventory.TryGetValue(goodsType, out int amount) ? amount : 0;
        }

        private static void AddInventory(Dictionary<GoodsType, int> inventory, GoodsType goodsType, int delta)
        {
            int next = Mathf.Max(0, GetInventory(inventory, goodsType) + delta);
            if (next == 0) inventory.Remove(goodsType);
            else inventory[goodsType] = next;
        }

        private static bool TryParseGoodsAction(string action, string prefix, out GoodsType goodsType)
        {
            goodsType = GoodsType.NullGoodsType;
            string normalizedPrefix = prefix + "-";
            if (!action.StartsWith(normalizedPrefix, StringComparison.Ordinal)) return false;
            string token = action.Substring(normalizedPrefix.Length);
            foreach (TrialGoodsRule rule in GoodsRules)
            {
                if (rule.Token == token)
                {
                    goodsType = rule.GoodsType;
                    return true;
                }
            }
            return false;
        }

        private static bool TryGetTechRule(string action, out TrialTechRule tech)
        {
            foreach (TrialTechRule rule in TechRules)
            {
                if (rule.Action == action)
                {
                    tech = rule;
                    return true;
                }
            }
            tech = default;
            return false;
        }

        private readonly struct TrialGoodsRule
        {
            public TrialGoodsRule(GoodsType goodsType, string token, string label, int cost, int price)
            {
                GoodsType = goodsType; Token = token; Label = label; Cost = cost; Price = price;
            }
            public GoodsType GoodsType { get; }
            public string Token { get; }
            public string Label { get; }
            public int Cost { get; }
            public int Price { get; }
        }

        private readonly struct TrialTechRule
        {
            public TrialTechRule(string action, string key, string label, int cost)
            {
                Action = action; Key = key; Label = label; Cost = cost;
            }
            public string Action { get; }
            public string Key { get; }
            public string Label { get; }
            public int Cost { get; }
        }
        [Serializable]
        private sealed class TrialOptions
        {
            public int teamId = 1;
            public int characterPlayerId = 1;
            public int sideFlag = 1;
        }

        private enum TrialObjectKind
        {
            None,
            Tile,
            Character,
            Factory,
            Resource,
            ComputeCenter,
            Market
        }

        private struct TrialSelection
        {
            public TrialObjectKind Kind;
            public long Guid;
            public long TeamId;
            public long PlayerId;
            public int Row;
            public int Col;
            public bool HasPosition;

            public static TrialSelection None => new TrialSelection { Kind = TrialObjectKind.None };
        }

        private sealed class TrialTeam
        {
            public TrialTeam(long teamId)
            {
                TeamId = teamId;
            }

            public long TeamId { get; }
            public long Score;
            public int Material;
            public int ComputePower;
            public readonly Dictionary<string, int> TechLevels = new Dictionary<string, int>();
        }

        private sealed class TrialFactory
        {
            public TrialFactory(long factoryId, long teamId, int row, int col)
            {
                FactoryId = factoryId;
                TeamId = teamId;
                Row = row;
                Col = col;
                Hp = FactoryHp;
                ComputingPower = InitialComputePower;
            }

            public long FactoryId { get; }
            public long TeamId { get; }
            public int Row { get; }
            public int Col { get; }
            public int Hp;
            public int Source;
            public int ComputingPower;
            public readonly Dictionary<GoodsType, int> ProductInventory = new Dictionary<GoodsType, int>();
        }

        private sealed class TrialResource
        {
            public TrialResource(long id, int row, int col)
            {
                Id = id;
                Row = row;
                Col = col;
            }

            public long Id { get; }
            public int Row { get; }
            public int Col { get; }
            public int Amount;
        }

        private sealed class TrialComputeCenter
        {
            public TrialComputeCenter(long centerId, int row, int col)
            {
                CenterId = centerId;
                Row = row;
                Col = col;
            }

            public long CenterId { get; }
            public int Row { get; }
            public int Col { get; }
            public long OwnerTeamId;
            public int OccupyProgress;
        }

        private sealed class TrialMarket
        {
            public TrialMarket(long marketId, int row, int col, MarketType marketType)
            {
                MarketId = marketId;
                Row = row;
                Col = col;
                MarketType = marketType;
            }

            public long MarketId { get; }
            public int Row { get; }
            public int Col { get; }
            public MarketType MarketType { get; }
            public readonly Dictionary<GoodsType, int> TradedQuantities = new Dictionary<GoodsType, int>();
        }

        private sealed class TrialPendingAction
        {
            public TrialPendingAction(string action, TrialObjectKind kind, long targetGuid, GoodsType goodsType = GoodsType.NullGoodsType)
            {
                Action = action;
                Kind = kind;
                TargetGuid = targetGuid;
                GoodsType = goodsType;
            }
            public string Action { get; }
            public TrialObjectKind Kind { get; }
            public long TargetGuid { get; }
            public GoodsType GoodsType { get; }
        }
        private sealed class TrialCharacter
        {
            public TrialCharacter(long guid, long teamId, long playerId, CharacterType type, int row, int col)
            {
                Guid = guid;
                TeamId = teamId;
                PlayerId = playerId;
                Type = type;
                Row = row;
                Col = col;
            }

            public long Guid { get; }
            public long TeamId { get; }
            public long PlayerId { get; }
            public CharacterType Type { get; }
            public int Row;
            public int Col;
            public int Hp;
            public CharacterState State;
            public readonly Queue<Vector2Int> Path = new Queue<Vector2Int>();
            public readonly Dictionary<GoodsType, int> GoodsLoad = new Dictionary<GoodsType, int>();
            public float MoveStepTimerMs;
            public TrialPendingAction PendingAction;
        }
    }
}
