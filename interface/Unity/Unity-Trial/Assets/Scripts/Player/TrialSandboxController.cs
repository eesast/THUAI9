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
        private const int CharacterAttack = 32;
        private const int CharacterAttackRange = 1000;
        private const int CharacterSpeed = 5200;
        private const int CharacterLoad = 8;
        private const int ResourceMaxAmount = 500;
        private const int FactoryHp = 100;
        private const int InitialMaterial = 120;
        private const int InitialComputePower = 100;
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
            FrameSourceHub.Reset(FrameSourceHub.SourceKind.Trial, "本地试玩", "试玩已启动：请选择对象或右键移动");
            running = true;
            CreateCharacter();
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
            if (currentSelection.Kind == TrialObjectKind.Character && currentSelection.TeamId == playerTeamId && characters.ContainsKey(currentSelection.Guid))
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
                        return $"选中角色：队伍 {character.TeamId} / 玩家 {character.PlayerId}\n生命：{character.Hp}/{CharacterHp}  状态：{FormatCharacterState(character.State)}\n位置：({character.Row}, {character.Col})";
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
                case TrialObjectKind.Tile:
                    return $"选中地图格：({selection.Row}, {selection.Col})\n类型：{FormatPlaceType(GetPlaceType(selection.Row, selection.Col))}";
                default:
                    return "未选中对象\n左键选择角色、工厂、资源点或算力中心；右键地图移动当前角色。";
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
                }
            }

            Vector2Int teamOneFactory = factoryCells.Count > 0 ? factoryCells[0] : new Vector2Int(3, 3);
            Vector2Int teamTwoFactory = factoryCells.Count > 1 ? factoryCells[factoryCells.Count - 1] : new Vector2Int(MapRows - 4, MapCols - 4);
            AddFactory(1, 1, teamOneFactory);
            AddFactory(2, 2, teamTwoFactory);
            AddNpcCharacter(2, 1, CharacterType.AutonomousCar);
        }

        private void AddFactory(long factoryId, long teamId, Vector2Int cell)
        {
            factories[factoryId] = new TrialFactory(factoryId, teamId, cell.x, cell.y);
            activeFactoryCells.Add(CellKey(cell.x, cell.y));
        }

        private void AddNpcCharacter(long teamId, long npcPlayerId, CharacterType type)
        {
            TrialFactory factory = GetTeamFactory(teamId);
            if (factory == null)
            {
                return;
            }

            Vector2Int spawn = FindNearestFreeCell(factory.Row, factory.Col, teamId == 1 ? 1 : -1);
            long characterId = NextGuid();
            characters[characterId] = new TrialCharacter(characterId, teamId, npcPlayerId, type, spawn.x, spawn.y)
            {
                Hp = CharacterHp,
                State = CharacterState.Idle
            };
        }

        public void CreateCharacter()
        {
            TrialFactory factory = GetPlayerFactory();
            if (factory == null)
            {
                StatusText = "试玩：没有可用工厂，无法创建角色";
                return;
            }

            if (activeCharacterGuid != 0 && characters.ContainsKey(activeCharacterGuid))
            {
                characters.Remove(activeCharacterGuid);
            }

            Vector2Int spawn = FindNearestFreeCell(factory.Row, factory.Col, sideFlag >= 0 ? 1 : -1);
            activeCharacterGuid = NextGuid();
            characters[activeCharacterGuid] = new TrialCharacter(activeCharacterGuid, playerTeamId, playerId, CharacterType.Robot, spawn.x, spawn.y)
            {
                Hp = CharacterHp,
                State = CharacterState.Idle
            };
            teams[playerTeamId].Score += 20;
            StatusText = $"已创建队伍 {playerTeamId} 的试玩角色";
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
                case "create":
                    CreateCharacter();
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
            if (!TryGetActiveCharacter(out TrialCharacter character))
            {
                StatusText = "请先创建角色";
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
                StatusText = "请先创建角色再采集";
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

            int amount = Mathf.Min(HarvestAmount + GetTechLevel(playerTeamId, "Carry") * 10, resource.Amount);
            resource.Amount -= amount;
            if (teams.TryGetValue(playerTeamId, out TrialTeam team))
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
                StatusText = "请先创建角色再占领算力中心";
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

            center.OwnerTeamId = playerTeamId;
            center.OccupyProgress = 100;
            if (teams.TryGetValue(playerTeamId, out TrialTeam team))
            {
                team.ComputePower += 50;
                team.Score += OccupyScore;
            }

            character.State = CharacterState.Ocuppying;
            StatusText = $"占领成功：算力中心归属队伍 {playerTeamId}";
        }

        private void Produce(TrialSelection selection)
        {
            TrialFactory factory = ResolveFactory(selection, requirePlayerTeam: true) ?? GetPlayerFactory();
            if (factory == null)
            {
                StatusText = "请先选择我方工厂";
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
            TrialFactory factory = ResolveFactory(selection, requirePlayerTeam: true) ?? GetPlayerFactory();
            if (factory == null)
            {
                StatusText = "请先选择我方工厂";
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
                StatusText = "请先创建角色再攻击";
                return;
            }

            TrialCharacter targetCharacter = ResolveEnemyCharacter(selection);
            TrialFactory targetFactory = targetCharacter == null ? ResolveEnemyFactory(selection) : null;
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

                int damage = CharacterAttack + GetTechLevel(playerTeamId, "Attack") * 8;
                targetCharacter.Hp = Mathf.Max(0, targetCharacter.Hp - damage);
                targetCharacter.State = targetCharacter.Hp <= 0 ? CharacterState.Deceased : CharacterState.KnockedBack;
                attacker.State = CharacterState.Attacking;
                teams[playerTeamId].Score += damage * 2;
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

                int damage = CharacterAttack;
                targetFactory.Hp = Mathf.Max(0, targetFactory.Hp - damage);
                attacker.State = CharacterState.Attacking;
                teams[playerTeamId].Score += damage;
                StatusText = $"攻击反馈：敌方工厂剩余血量 {targetFactory.Hp}";
                return;
            }

            StatusText = "没有可攻击目标";
        }

        private void Recover()
        {
            if (!TryGetActiveCharacter(out TrialCharacter character))
            {
                StatusText = "请先创建角色再恢复";
                return;
            }

            TrialTeam team = teams[playerTeamId];
            if (character.Hp >= CharacterHp)
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
            character.Hp = Mathf.Min(CharacterHp, character.Hp + 50);
            character.State = CharacterState.Idle;
            StatusText = $"恢复完成：生命值 {character.Hp}/{CharacterHp}";
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
                case "create":
                    return selection.Kind == TrialObjectKind.None || selection.Kind == TrialObjectKind.Factory && selection.TeamId == playerTeamId;
                case "move":
                    return TryGetActiveCharacter(out _) && selection.HasPosition;
                case "harvest":
                    return TryGetActiveCharacter(out _) && (selection.Kind == TrialObjectKind.Resource || resources.Values.Any(r => r.Amount > 0));
                case "occupy":
                    return TryGetActiveCharacter(out _) && (selection.Kind == TrialObjectKind.ComputeCenter || computeCenters.Count > 0);
                case "produce":
                    return ResolveFactory(selection, requirePlayerTeam: true) != null || GetPlayerFactory() != null;
                case "upgrade":
                    return (ResolveFactory(selection, requirePlayerTeam: true) != null || GetPlayerFactory() != null) && teams.ContainsKey(playerTeamId);
                case "attack":
                    return TryGetActiveCharacter(out _) && (ResolveEnemyCharacter(selection) != null || ResolveEnemyFactory(selection) != null || characters.Values.Any(c => c.TeamId != playerTeamId && c.Hp > 0));
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
                frame.ObjMessage.Add(new MessageOfObj
                {
                    ComputeCenterMessage = new MessageOfComputeCenter
                    {
                        CenterId = center.CenterId,
                        X = center.Row,
                        Y = center.Col,
                        OwnerTeamId = center.OwnerTeamId,
                        OccupyProgress = center.OccupyProgress
                    }
                });
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
            MessageOfFactory message = new MessageOfFactory
            {
                FactoryId = factory.FactoryId,
                TeamId = factory.TeamId,
                X = factory.Row,
                Y = factory.Col,
                Hp = factory.Hp,
                Robust = 1,
                Storage = 100,
                Efficiency = 100 + GetTechLevel(factory.TeamId, "Production") * 15,
                Source = factory.Source,
                ComputingPower = factory.ComputingPower,
                CanProduce = factory.TeamId == playerTeamId,
                CanRecruit = factory.TeamId == playerTeamId
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
            return new MessageOfResource
            {
                Id = Mathf.Clamp((int)resource.Id, 0, int.MaxValue),
                X = resource.Row,
                Y = resource.Col,
                ResourceType = ResourceType.LargeResource,
                RemainingAmount = resource.Amount,
                MaxAmount = ResourceMaxAmount,
                ResourceState = resource.Amount > 0 ? ResourceState.Harvestable : ResourceState.Harvested
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
                CommonAttack = CharacterAttack,
                CommonAttackCd = 1000,
                CommonAttackRange = CharacterAttackRange,
                Speed = CharacterSpeed,
                CurrentLoad = 0,
                CarryCapacity = CharacterLoad,
                ViewRange = 9000,
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

        private TrialCharacter ResolveEnemyCharacter(TrialSelection selection)
        {
            if (selection.Kind == TrialObjectKind.Character && characters.TryGetValue(selection.Guid, out TrialCharacter character) && character.TeamId != playerTeamId && character.Hp > 0)
            {
                return character;
            }

            return null;
        }

        private TrialFactory ResolveEnemyFactory(TrialSelection selection)
        {
            if (selection.Kind == TrialObjectKind.Factory && factories.TryGetValue(selection.Guid, out TrialFactory factory) && factory.TeamId != playerTeamId && factory.Hp > 0)
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

        private bool TryGetActiveCharacter(out TrialCharacter character)
        {
            if (activeCharacterGuid != 0 && characters.TryGetValue(activeCharacterGuid, out character) && character.Hp > 0)
            {
                return true;
            }

            character = characters.Values.FirstOrDefault(c => c.TeamId == playerTeamId && c.Hp > 0);
            if (character != null)
            {
                activeCharacterGuid = character.Guid;
                return true;
            }

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
                if (character.TeamId == playerTeamId || character.Hp <= 0)
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

        private Vector2Int FindNearestFreeCell(int row, int col, int preferredDirection)
        {
            int direction = preferredDirection == 0 ? 1 : Math.Sign(preferredDirection);
            Vector2Int[] offsets =
            {
                new Vector2Int(0, direction),
                new Vector2Int(direction, 0),
                new Vector2Int(0, -direction),
                new Vector2Int(-direction, 0),
                new Vector2Int(1, 1),
                new Vector2Int(-1, -1),
                new Vector2Int(1, -1),
                new Vector2Int(-1, 1)
            };

            foreach (Vector2Int offset in offsets)
            {
                int candidateRow = Mathf.Clamp(row + offset.x, 0, MapRows - 1);
                int candidateCol = Mathf.Clamp(col + offset.y, 0, MapCols - 1);
                if (!IsBlocked(candidateRow, candidateCol))
                {
                    return new Vector2Int(candidateRow, candidateCol);
                }
            }

            return new Vector2Int(Mathf.Clamp(row, 0, MapRows - 1), Mathf.Clamp(col, 0, MapCols - 1));
        }

        private bool IsNear(TrialCharacter character, int row, int col, int range)
        {
            return Manhattan(character.Row, character.Col, row, col) <= range + 1;
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
            if (place == PlaceType.Barrier)
            {
                return true;
            }

            return place == PlaceType.Factory && !activeFactoryCells.Contains(CellKey(row, col));
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
            ComputeCenter
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
