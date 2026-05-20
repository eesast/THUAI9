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
        private const int ProduceCost = 45;
        private const int TechCost = 60;
        private const int RecoveryCost = 20;
        private const int HarvestAmount = 40;
        private const int OccupyScore = 150;
        private const int InteractionRangeCells = 1;

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
        private long activeCharacterGuid;
        private int nextTechIndex;

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

            elapsedMs += Time.deltaTime * 1000f;
            HandleKeyboardShortcuts();

            if (elapsedMs - lastSubmittedElapsedMs >= FrameDurationMs)
            {
                SubmitFrame();
                lastSubmittedElapsedMs = elapsedMs;
            }
        }

        public void StartTrial(string optionsJson = null)
        {
            ApplyOptions(optionsJson);
            ResetState();
            BuildWorld();
            StatusText = "试玩已启动：请选择队伍工厂创建角色";
            FrameSourceHub.Reset(FrameSourceHub.SourceKind.Trial, "本地试玩", "试玩已启动：请选择队伍工厂创建角色");
            running = true;
            SubmitFrame();
        }

        public void ResetTrial()
        {
            StartTrial(null);
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
            if (currentSelection.Kind == TrialObjectKind.Character && characters.ContainsKey(currentSelection.Guid))
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
                        return $"选中角色：队伍 {character.TeamId} / 玩家 {character.PlayerId}\n类型：{FormatCharacterType(character.Type)}  生命：{character.Hp}/{GetCharacterMaxHp(character.Type)}\n状态：{FormatCharacterState(character.State)}  位置：({character.Row}, {character.Col})";
                    }
                    return $"选中角色：队伍 {selection.TeamId}\n位置：({selection.Row}, {selection.Col})";
                case TrialObjectKind.Factory:
                    if (factories.TryGetValue(selection.Guid, out TrialFactory factory))
                    {
                        return $"选中工厂：队伍 {factory.TeamId}\n血量：{factory.Hp}/{FactoryHp}  库存：{FormatInventory(factory)}\n位置：({factory.Row}, {factory.Col})";
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
                        return $"选中市场：{FormatMarketType(market.MarketType)}\n位置：({market.Row}, {market.Col})\n当前 Trial 先展示市场对象与价格，交易闭环后续接入。";
                    }
                    return $"选中市场：({selection.Row}, {selection.Col})";
                case TrialObjectKind.Tile:
                    return $"选中地图格：({selection.Row}, {selection.Col})\n类型：{FormatPlaceType(GetPlaceType(selection.Row, selection.Col))}";
                default:
                    return "未选中对象\n左键选择队伍工厂创建角色；选中角色后可用 WASD / 方向键单格移动。";
            }
        }

        public bool CanExecuteAction(string action, WorldObjectInfo info, Vector2Int? tile)
        {
            return CanExecuteAction(action, BuildSelection(info, tile));
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
                StatusText = "请先创建角色";
                return;
            }

            MoveCharacterToTile(character, row, col, "右键移动");
            SubmitFrame();
        }

        private void ResetState()
        {
            elapsedMs = 0f;
            lastSubmittedElapsedMs = -FrameDurationMs;
            nextGuid = 1000;
            activeCharacterGuid = 0;
            nextTechIndex = 0;
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
                StartTrial(null);
            }

            string action = NormalizeAction(rawAction);
            switch (action)
            {
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
                    Produce(selection);
                    break;
                case "upgrade":
                    UpgradeTech(selection);
                    break;
                case "attack":
                    Attack(selection);
                    break;
                case "recover":
                    Recover();
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
            if (selection.Kind == TrialObjectKind.Tile || selection.HasPosition)
            {
                MoveSelectedOrPlayerToTile(selection.Row, selection.Col);
            }
            else
            {
                StatusText = "请先选中地图格，或右键目标位置移动";
            }
        }

        private void MoveBy(int dRow, int dCol)
        {
            if (!TryGetSelectedCharacter(out TrialCharacter character))
            {
                StatusText = "请先左键选中一个已创建角色";
                return;
            }

            MoveCharacterToTile(character, character.Row + dRow, character.Col + dCol, "键盘移动");
        }

        private void MoveCharacterToTile(TrialCharacter character, int row, int col, string verb)
        {
            row = Mathf.Clamp(row, 0, MapRows - 1);
            col = Mathf.Clamp(col, 0, MapCols - 1);
            if (IsBlocked(row, col))
            {
                StatusText = $"{verb}失败：({row}, {col}) 是障碍或未开放工厂";
                return;
            }

            character.Row = row;
            character.Col = col;
            character.State = CharacterState.Moving;
            StatusText = $"{verb}到 ({row}, {col})";
        }

        private void Harvest(TrialSelection selection)
        {
            if (!TryGetActiveCharacter(out TrialCharacter character))
            {
                StatusText = "请先创建并选中角色再采集";
                return;
            }

            TrialResource resource = ResolveResource(selection);
            if (resource == null || resource.Amount <= 0)
            {
                StatusText = "没有可采集资源";
                return;
            }

            if (!IsNear(character, resource.Row, resource.Col, InteractionRangeCells))
            {
                MoveCharacterNear(character, resource.Row, resource.Col, "已移动到资源附近，再点击采集");
                return;
            }

            int amount = Mathf.Min(HarvestAmount + GetTechLevel(character.TeamId, "Carry") * 10, resource.Amount);
            resource.Amount -= amount;
            if (teams.TryGetValue(character.TeamId, out TrialTeam team))
            {
                team.Material += amount;
                team.Score += amount * 2;
            }

            character.State = CharacterState.Harvesting;
            StatusText = $"采集成功：获得原料 {amount}，资源剩余 {resource.Amount}";
        }

        private void Occupy(TrialSelection selection)
        {
            if (!TryGetActiveCharacter(out TrialCharacter character))
            {
                StatusText = "请先创建并选中角色再占领算力中心";
                return;
            }

            if (character.Type == CharacterType.AutonomousCar)
            {
                StatusText = "THUAI9 规则：无人车不能占领算力中心，请使用无人机或机器人";
                return;
            }

            TrialComputeCenter center = ResolveComputeCenter(selection);
            if (center == null)
            {
                StatusText = "没有可占领算力中心";
                return;
            }

            if (!IsNear(character, center.Row, center.Col, InteractionRangeCells))
            {
                MoveCharacterNear(character, center.Row, center.Col, "已移动到算力中心附近，再点击占领");
                return;
            }

            center.OwnerTeamId = character.TeamId;
            center.OccupyProgress = 100;
            if (teams.TryGetValue(character.TeamId, out TrialTeam team))
            {
                team.ComputePower += 50;
                team.Score += OccupyScore;
            }

            character.State = CharacterState.Ocuppying;
            StatusText = $"占领成功：算力中心归属队伍 {character.TeamId}";
        }

        private void Produce(TrialSelection selection)
        {
            TrialFactory factory = ResolveFactory(selection, requirePlayerTeam: false);
            if (factory == null)
            {
                StatusText = "请先选择队伍工厂";
                return;
            }

            TrialTeam team = teams[factory.TeamId];
            int cost = Mathf.Max(15, ProduceCost - GetTechLevel(factory.TeamId, "Production") * 8);
            if (team.Material < cost)
            {
                StatusText = $"原料不足：生产需要 {cost}";
                return;
            }

            team.Material -= cost;
            team.Score += 120;
            factory.Source += 1;
            if (!factory.ProductInventory.ContainsKey(GoodsType.Food))
            {
                factory.ProductInventory[GoodsType.Food] = 0;
            }
            factory.ProductInventory[GoodsType.Food] += 1;
            StatusText = $"工厂生产完成：消耗原料 {cost}，产物 +1";
        }

        private void UpgradeTech(TrialSelection selection)
        {
            TrialFactory factory = ResolveFactory(selection, requirePlayerTeam: false);
            if (factory == null)
            {
                StatusText = "请先选择队伍工厂";
                return;
            }

            TrialTeam team = teams[factory.TeamId];
            if (team.Material < TechCost)
            {
                StatusText = $"原料不足：升级科技需要 {TechCost}";
                return;
            }

            string[] techs = { "MoveSpeed", "Carry", "Production", "Hp" };
            string tech = techs[nextTechIndex % techs.Length];
            nextTechIndex++;
            team.Material -= TechCost;
            team.TechLevels[tech] = GetTechLevel(factory.TeamId, tech) + 1;
            team.Score += 180;
            StatusText = $"科技升级：{FormatTechName(tech)} Lv.{team.TechLevels[tech]}";
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
                    MoveCharacterNear(attacker, targetCharacter.Row, targetCharacter.Col, "已移动到敌方角色附近，再点击攻击");
                    return;
                }

                int damage = GetCharacterAttack(attacker.Type) + GetTechLevel(attacker.TeamId, "Attack") * 8;
                targetCharacter.Hp = Mathf.Max(0, targetCharacter.Hp - damage);
                targetCharacter.State = targetCharacter.Hp <= 0 ? CharacterState.Deceased : CharacterState.KnockedBack;
                attacker.State = CharacterState.Attacking;
                teams[attacker.TeamId].Score += damage * 2;
                StatusText = targetCharacter.Hp <= 0
                    ? "攻击反馈：敌方角色已失去行动能力"
                    : $"攻击反馈：造成 {damage} 伤害，目标剩余 {targetCharacter.Hp}";
                return;
            }

            if (targetFactory != null)
            {
                if (!IsNear(attacker, targetFactory.Row, targetFactory.Col, InteractionRangeCells + 1))
                {
                    MoveCharacterNear(attacker, targetFactory.Row, targetFactory.Col, "已移动到敌方工厂附近，再点击攻击");
                    return;
                }

                int damage = GetCharacterAttack(attacker.Type);
                targetFactory.Hp = Mathf.Max(0, targetFactory.Hp - damage);
                attacker.State = CharacterState.Attacking;
                teams[attacker.TeamId].Score += damage;
                StatusText = $"攻击反馈：敌方工厂剩余血量 {targetFactory.Hp}";
                return;
            }

            StatusText = "没有可攻击目标";
        }

        private void Recover()
        {
            if (!TryGetActiveCharacter(out TrialCharacter character))
            {
                StatusText = "请先创建并选中角色再恢复";
                return;
            }

            TrialTeam team = teams[character.TeamId];
            int maxHp = GetCharacterMaxHp(character.Type);
            if (character.Hp >= maxHp)
            {
                StatusText = "当前角色生命值已满";
                return;
            }

            if (team.Material < RecoveryCost)
            {
                StatusText = $"原料不足：恢复需要 {RecoveryCost}";
                return;
            }

            team.Material -= RecoveryCost;
            character.Hp = Mathf.Min(maxHp, character.Hp + 50);
            character.State = CharacterState.Idle;
            StatusText = $"恢复完成：生命值 {character.Hp}/{maxHp}";
        }

        private void StopCurrentAction()
        {
            if (TryGetActiveCharacter(out TrialCharacter character))
            {
                character.State = CharacterState.Idle;
                StatusText = "已停止当前动作";
            }
            else
            {
                StatusText = "当前没有角色动作";
            }
        }

        private void HandleKeyboardShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                ResetTrial();
                return;
            }

            if (Input.GetKeyDown(KeyCode.H))
            {
                ExecuteAction("harvest", currentSelection);
            }
            else if (Input.GetKeyDown(KeyCode.O))
            {
                ExecuteAction("occupy", currentSelection);
            }
            else if (Input.GetKeyDown(KeyCode.P))
            {
                ExecuteAction("produce", currentSelection);
            }
            else if (Input.GetKeyDown(KeyCode.U))
            {
                ExecuteAction("upgrade", currentSelection);
            }
            else if (Input.GetKeyDown(KeyCode.F))
            {
                ExecuteAction("attack", currentSelection);
            }
            else if (Input.GetKeyDown(KeyCode.G))
            {
                ExecuteAction("recover", currentSelection);
            }
            else if (Input.GetKeyDown(KeyCode.Space))
            {
                ExecuteAction("stop", currentSelection);
            }
            else if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
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
            string action = NormalizeAction(rawAction);
            switch (action)
            {
                case "create-drone":
                case "create-robot":
                case "create-car":
                case "create":
                    return CanCreateCharacter(selection);
                case "move":
                    return TryGetActiveCharacter(out _) && selection.HasPosition;
                case "harvest":
                    return TryGetActiveCharacter(out _) && (selection.Kind == TrialObjectKind.Resource || resources.Values.Any(r => r.Amount > 0));
                case "occupy":
                    return TryGetActiveCharacter(out _) && (selection.Kind == TrialObjectKind.ComputeCenter || computeCenters.Count > 0);
                case "produce":
                    return ResolveFactory(selection, requirePlayerTeam: false) != null;
                case "upgrade":
                    return ResolveFactory(selection, requirePlayerTeam: false) != null;
                case "attack":
                    return TryGetActiveCharacter(out TrialCharacter attacker)
                        && (ResolveEnemyCharacter(selection, attacker.TeamId) != null
                            || ResolveEnemyFactory(selection, attacker.TeamId) != null
                            || characters.Values.Any(c => c.TeamId != attacker.TeamId && c.Hp > 0));
                case "recover":
                case "stop":
                    return TryGetActiveCharacter(out _);
                default:
                    return false;
            }
        }

        private TrialSelection BuildSelection(WorldObjectInfo info, Vector2Int? tile)
        {
            if (info != null)
            {
                TrialObjectKind kind = ParseObjectKind(info.objectType);
                return new TrialSelection
                {
                    Kind = kind,
                    Guid = info.guid,
                    TeamId = info.teamId,
                    PlayerId = info.playerId,
                    Row = info.gridX,
                    Col = info.gridY,
                    HasPosition = true
                };
            }

            if (tile.HasValue)
            {
                return new TrialSelection
                {
                    Kind = TrialObjectKind.Tile,
                    Row = tile.Value.x,
                    Col = tile.Value.y,
                    HasPosition = true
                };
            }

            return TrialSelection.None;
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
            if (string.IsNullOrWhiteSpace(action))
            {
                return string.Empty;
            }

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
                default:
                    return normalized;
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
                Storage = 100,
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

        private static MessageOfMarket BuildMarketMessage(TrialMarket market)
        {
            Vector2Int gamePosition = GridToGameCell(market.Row, market.Col);
            MessageOfMarket message = new MessageOfMarket
            {
                MarketId = market.MarketId,
                X = gamePosition.x,
                Y = gamePosition.y,
                MarketType = market.MarketType
            };

            message.PriceList.Add(BuildPriceEntry(GoodsType.Semiconductor, 80));
            message.PriceList.Add(BuildPriceEntry(GoodsType.Medicine, 60));
            message.PriceList.Add(BuildPriceEntry(GoodsType.Toys, 40));
            message.PriceList.Add(BuildPriceEntry(GoodsType.Clothes, 50));
            message.PriceList.Add(BuildPriceEntry(GoodsType.Food, 30));
            return message;
        }

        private static MessageOfMarket.Types.PriceEntry BuildPriceEntry(GoodsType goodsType, int basePrice)
        {
            return new MessageOfMarket.Types.PriceEntry
            {
                GoodsType = goodsType,
                Price = basePrice,
                TradedQuantity = 0
            };
        }

        private static MessageOfCharacter BuildCharacterMessage(TrialCharacter character)
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
                CommonAttack = GetCharacterAttack(character.Type),
                CommonAttackCd = character.State == CharacterState.Attacking ? 1500 : 1000,
                CommonAttackRange = CharacterAttackRange,
                Speed = CharacterSpeed,
                CurrentLoad = character.State == CharacterState.Harvesting ? GetCharacterCarry(character.Type) : 0,
                CarryCapacity = GetCharacterCarry(character.Type),
                ViewRange = GetCharacterViewRange(character.Type),
                HarvestRatePerSec = 20
            };
        }

        private TrialResource ResolveResource(TrialSelection selection)
        {
            if (selection.Kind == TrialObjectKind.Resource && resources.TryGetValue(selection.Guid, out TrialResource selected) && selected.Amount > 0)
            {
                return selected;
            }

            return TryGetActiveCharacter(out TrialCharacter character) ? FindNearestResource(character) : null;
        }

        private TrialComputeCenter ResolveComputeCenter(TrialSelection selection)
        {
            if (selection.Kind == TrialObjectKind.ComputeCenter && computeCenters.TryGetValue(selection.Guid, out TrialComputeCenter selected))
            {
                return selected;
            }

            return TryGetActiveCharacter(out TrialCharacter character) ? FindNearestComputeCenter(character) : null;
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

        private void MoveCharacterNear(TrialCharacter character, int targetRow, int targetCol, string status)
        {
            Vector2Int near = FindNearestFreeCell(targetRow, targetCol, targetRow <= character.Row ? -1 : 1);
            character.Row = near.x;
            character.Col = near.y;
            character.State = CharacterState.Moving;
            StatusText = status;
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
            for (int radius = 1; radius <= 6; radius++)
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
                    if (IsCellAvailable(candidateRow, candidateCol))
                    {
                        return new Vector2Int(candidateRow, candidateCol);
                    }
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
            if (row < 0 || col < 0 || row >= MapRows || col >= MapCols)
            {
                return true;
            }

            PlaceType place = GetPlaceType(row, col);
            return place != PlaceType.Space && place != PlaceType.Bush;
        }

        private bool IsCellAvailable(int row, int col)
        {
            return !IsBlocked(row, col) && !characters.Values.Any(c => c.Hp > 0 && c.Row == row && c.Col == col);
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
                case "MoveSpeed":
                    return "移动速度";
                case "Carry":
                    return "采集载重";
                case "Production":
                    return "生产效率";
                case "Hp":
                    return "生命强化";
                case "Attack":
                    return "攻击强化";
                default:
                    return tech;
            }
        }

        private static string FormatInventory(TrialFactory factory)
        {
            if (factory.ProductInventory.Count == 0)
            {
                return "无";
            }

            return string.Join("/", factory.ProductInventory.Select(pair => pair.Key + "×" + pair.Value));
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
        }
    }
}
