using System;
using System.Collections.Generic;
using Protobuf;
using THUAI9.Unity.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace THUAI9.Unity.Player
{
    public sealed class TrialSandboxController : MonoBehaviour
    {
        private const int MapRows = 50;
        private const int MapCols = 50;
        private const int FrameDurationMs = 50;
        private const int CharacterHp = 150;
        private const int CharacterAttack = 30;
        private const int CharacterAttackRange = 1000;
        private const int CharacterSpeed = 5000;
        private const int CharacterLoad = 5;
        private const int ResourceMaxAmount = 500;
        private const int FactoryHp = 100;
        private const int InitialMaterial = 120;
        private const int InitialComputePower = 100;
        private const int ProduceCost = 50;
        private const int TechCost = 60;
        private const int ComputeCenterOccupyTimeMs = 10000;
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
        private readonly Dictionary<long, TrialResource> resources = new Dictionary<long, TrialResource>();
        private readonly Dictionary<long, TrialComputeCenter> computeCenters = new Dictionary<long, TrialComputeCenter>();
        private readonly List<TrialFactory> factories = new List<TrialFactory>();
        private TrialCharacter character;
        private float elapsedMs;
        private float lastSubmittedElapsedMs;
        private bool running;
        private long nextGuid = 1000;
        private long playerTeamId = 1;
        private long playerId = 1;
        private int sideFlag = 1;
        public string StatusText { get; private set; } = "试玩：未启动";

        private void Update()
        {
            if (!running) return;
            elapsedMs += Time.deltaTime * 1000f;
            HandleKeyboard();
            if (Input.GetMouseButtonDown(1) && !IsPointerOverUi()) MoveToMouseCell();
            if (elapsedMs - lastSubmittedElapsedMs >= FrameDurationMs)
            {
                SubmitFrame();
            }
        }

        public void StartTrial(string optionsJson = null)
        {
            ApplyOptions(optionsJson);
            running = true;
            elapsedMs = 0f;
            lastSubmittedElapsedMs = -FrameDurationMs;
            BuildWorld();
            StatusText = string.Format("试玩：已启动，队伍 {0}，sideFlag={1}，使用正式 50x50 地图复杂度", playerTeamId, sideFlag);
            FrameSourceHub.Reset(FrameSourceHub.SourceKind.Live, "本地试玩", StatusText);
            CreateCharacter();
            SubmitFrame();
        }

        public void StopTrial()
        {
            running = false;
            StatusText = "试玩：已停止";
            FrameSourceHub.Reset(FrameSourceHub.SourceKind.None, "未选择", StatusText);
        }

        public void HandleAction(string action)
        {
            switch ((action ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "create-character": case "create": CreateCharacter(); break;
                case "harvest": Harvest(); break;
                case "occupy": Occupy(); break;
                case "produce": Produce(); break;
                case "uplevel-tech": case "upgrade": UpgradeTech(); break;
                case "attack": Attack(); break;
                case "recover": Recover(); break;
                case "end-all-action": SetCharacterState(CharacterState.Idle, "试玩：已结束当前动作"); break;
                case "up": MoveBy(-1, 0); break;
                case "down": MoveBy(1, 0); break;
                case "left": MoveBy(0, -1); break;
                case "right": MoveBy(0, 1); break;
                default: StatusText = "试玩：未知动作 " + action; break;
            }
        }

        private void ApplyOptions(string optionsJson)
        {
            if (string.IsNullOrWhiteSpace(optionsJson))
            {
                return;
            }

            try
            {
                TrialOptions options = JsonUtility.FromJson<TrialOptions>(optionsJson);
                if (options == null)
                {
                    return;
                }

                playerTeamId = Mathf.Clamp((int)(options.teamId > 0 ? options.teamId : 1), 1, 4);
                playerId = Math.Max(1, options.characterPlayerId > 0 ? options.characterPlayerId : 1);
                sideFlag = options.sideFlag;
            }
            catch
            {
                playerTeamId = 1;
                playerId = 1;
                sideFlag = 1;
            }
        }

        private TrialTeam GetPlayerTeam()
        {
            long teamId = character != null ? character.TeamId : playerTeamId;
            return teams.TryGetValue(teamId, out TrialTeam team) ? team : teams[1];
        }

        public void CreateCharacter()
        {
            if (!running || teams.Count == 0)
            {
                StartTrial();
                return;
            }

            TrialFactory spawn = factories.Find(f => f.TeamId == playerTeamId) ?? (factories.Count > 0 ? factories[0] : new TrialFactory { TeamId = playerTeamId, Row = 3, Col = 3, Hp = FactoryHp });
            character = new TrialCharacter { Guid = nextGuid++, TeamId = playerTeamId, PlayerId = playerId, Row = spawn.Row, Col = spawn.Col, Hp = CharacterHp, State = CharacterState.Idle };
            StatusText = string.Format("试玩：已创建队伍 {0} 机器人，WASD/方向键/右键移动", playerTeamId);
            SubmitFrame();
        }

        public void MoveBy(int dRow, int dCol)
        {
            if (character == null) CreateCharacter();
            int nr = Mathf.Clamp(character.Row + dRow, 0, MapRows - 1);
            int nc = Mathf.Clamp(character.Col + dCol, 0, MapCols - 1);
            if (IsBlocked(nr, nc)) { StatusText = "试玩：目标格是障碍，无法移动"; return; }
            character.Row = nr;
            character.Col = nc;
            character.State = CharacterState.Moving;
            StatusText = string.Format("试玩：移动到 ({0}, {1})", nr, nc);
            SubmitFrame();
        }

        public void Harvest()
        {
            if (character == null) CreateCharacter();
            TrialResource target = FindNearestResource();
            if (target == null) { StatusText = "试玩：附近没有可采集资源"; return; }
            int amount = Mathf.Min(CharacterLoad, target.RemainingAmount);
            target.RemainingAmount -= amount;
            target.State = target.RemainingAmount <= 0 ? ResourceState.Harvested : ResourceState.BeingHarvested;
            TrialTeam team = teams[character.TeamId];
            team.Material += amount;
            team.Score += amount * 10;
            character.Row = target.Row;
            character.Col = target.Col;
            character.State = CharacterState.Harvesting;
            StatusText = string.Format("试玩：采集资源 +{0}，原料 {1}", amount, team.Material);
            SubmitFrame();
        }

        public void Occupy()
        {
            if (character == null) CreateCharacter();
            TrialComputeCenter center = FindNearestComputeCenter();
            if (center == null) { StatusText = "试玩：没有可占领算力中心"; return; }
            center.OwnerTeamId = character.TeamId;
            center.OccupyProgress = 100;
            TrialTeam team = teams[character.TeamId];
            team.ComputePower += 20;
            team.Score += 200;
            character.Row = center.Row;
            character.Col = center.Col;
            character.State = CharacterState.Ocuppying;
            StatusText = string.Format("试玩：占领算力中心，正式规则占领耗时约 {0} 秒", ComputeCenterOccupyTimeMs / 1000);
            SubmitFrame();
        }

        public void Produce()
        {
            TrialTeam team = GetPlayerTeam();
            if (team.Material < ProduceCost) { StatusText = string.Format("试玩：生产需要 {0} 原料", ProduceCost); return; }
            team.Material -= ProduceCost;
            team.Score += 120;
            StatusText = string.Format("试玩：生产成功，消耗 {0} 原料", ProduceCost);
            SubmitFrame();
        }

        public void UpgradeTech()
        {
            TrialTeam team = GetPlayerTeam();
            if (team.Material < TechCost) { StatusText = string.Format("试玩：升级需要 {0} 原料", TechCost); return; }
            team.Material -= TechCost;
            int level;
            team.TechLevels.TryGetValue("IncreaseMoveSpeed", out level);
            team.TechLevels["IncreaseMoveSpeed"] = Mathf.Min(2, level + 1);
            team.Score += 180;
            StatusText = "试玩：移动科技等级 +1";
            SubmitFrame();
        }

        public void Attack() { SetCharacterState(CharacterState.Attacking, "试玩：攻击反馈已触发（本地沙盒不做完整战斗判定）"); }
        public void Recover() { if (character != null) character.Hp = CharacterHp; SetCharacterState(CharacterState.Idle, "试玩：角色已恢复"); }

        private void BuildWorld()
        {
            teams.Clear(); resources.Clear(); computeCenters.Clear(); factories.Clear(); nextGuid = 1000;
            for (int i = 1; i <= 4; i++) teams[i] = new TrialTeam { TeamId = i, Material = InitialMaterial, ComputePower = InitialComputePower };
            long resourceId = 1, centerId = 1, factoryId = 1;
            for (int r = 0; r < MapRows; r++)
            {
                for (int c = 0; c < MapCols; c++)
                {
                    int place = OfficialMap[r][c];
                    if (place == (int)PlaceType.Factory) factories.Add(new TrialFactory { FactoryId = factoryId, TeamId = Math.Min(factoryId, 4), Row = r, Col = c, Hp = FactoryHp });
                    else if (place == (int)PlaceType.Resource) resources[resourceId] = new TrialResource { Id = resourceId++, Row = r, Col = c, Type = ResourceType.MediumResource, RemainingAmount = ResourceMaxAmount, MaxAmount = ResourceMaxAmount, State = ResourceState.Harvestable };
                    else if (place == (int)PlaceType.ComputeCenter) computeCenters[centerId] = new TrialComputeCenter { Id = centerId++, Row = r, Col = c, OwnerTeamId = 0, OccupyProgress = 0 };
                    if (place == (int)PlaceType.Factory) factoryId++;
                }
            }
        }

        private void SubmitFrame()
        {
            if (!running) return;
            lastSubmittedElapsedMs = elapsedMs;
            FrameSourceHub.SubmitImmediate(BuildFrame(), Mathf.RoundToInt(elapsedMs / FrameDurationMs), Mathf.RoundToInt(elapsedMs), StatusText);
        }

        private MessageToClient BuildFrame()
        {
            MessageToClient frame = new MessageToClient { GameState = GameState.GameRunning, AllMessage = BuildAllMessage() };
            frame.ObjMessage.Add(new MessageOfObj { MapMessage = BuildMapMessage() });
            foreach (TrialTeam team in teams.Values) frame.ObjMessage.Add(new MessageOfObj { TeamMessage = BuildTeamMessage(team) });
            foreach (TrialFactory factory in factories) frame.ObjMessage.Add(new MessageOfObj { FactoryMessage = BuildFactoryMessage(factory) });
            foreach (TrialResource resource in resources.Values) frame.ObjMessage.Add(new MessageOfObj { ResourceMessage = BuildResourceMessage(resource) });
            foreach (TrialComputeCenter center in computeCenters.Values) frame.ObjMessage.Add(new MessageOfObj { ComputeCenterMessage = BuildComputeCenterMessage(center) });
            if (character != null) frame.ObjMessage.Add(new MessageOfObj { CharacterMessage = BuildCharacterMessage(character) });
            return frame;
        }

        private MessageOfMap BuildMapMessage()
        {
            MessageOfMap map = new MessageOfMap { Height = MapRows, Width = MapCols };
            for (int r = 0; r < MapRows; r++)
            {
                MessageOfMap.Types.Row row = new MessageOfMap.Types.Row();
                for (int c = 0; c < MapCols; c++) row.Cols.Add((PlaceType)OfficialMap[r][c]);
                map.Rows.Add(row);
            }
            return map;
        }

        private MessageOfAll BuildAllMessage()
        {
            MessageOfAll all = new MessageOfAll { GameTime = CoreParam.ClampDisplayGameMilliseconds(Mathf.RoundToInt(elapsedMs)) };
            for (int i = 1; i <= 4; i++)
            {
                TrialTeam t = teams[i];
                MessageOfAll.Types.TeamInfo info = new MessageOfAll.Types.TeamInfo { Score = t.Score, Material = t.Material, ComputePower = t.ComputePower, FactoryHp = FactoryHp };
                foreach (KeyValuePair<string, int> kv in t.TechLevels) info.TechLevels[kv.Key] = kv.Value;
                all.Teams.Add(info);
            }
            return all;
        }

        private static MessageOfTeam BuildTeamMessage(TrialTeam t)
        {
            MessageOfTeam msg = new MessageOfTeam { TeamId = t.TeamId, PlayerId = 1, Score = t.Score, Material = t.Material, ComputePower = t.ComputePower };
            foreach (KeyValuePair<string, int> kv in t.TechLevels) msg.TechLevels[kv.Key] = kv.Value;
            return msg;
        }
        private static MessageOfFactory BuildFactoryMessage(TrialFactory f) { Vector2 p = Tool.GridToGame(f.Row, f.Col); return new MessageOfFactory { FactoryId = f.FactoryId, TeamId = f.TeamId, X = (int)p.x, Y = (int)p.y, Hp = f.Hp, Robust = 20, Storage = 5, Efficiency = 1, Source = 0, ComputingPower = InitialComputePower, CanProduce = true, CanRecruit = true }; }
        private static MessageOfResource BuildResourceMessage(TrialResource r) { Vector2 p = Tool.GridToGame(r.Row, r.Col); return new MessageOfResource { Id = (int)r.Id, ResourceType = r.Type, ResourceState = r.State, X = (int)p.x, Y = (int)p.y, RemainingAmount = r.RemainingAmount, MaxAmount = r.MaxAmount }; }
        private static MessageOfComputeCenter BuildComputeCenterMessage(TrialComputeCenter c) { Vector2 p = Tool.GridToGame(c.Row, c.Col); return new MessageOfComputeCenter { CenterId = c.Id, X = (int)p.x, Y = (int)p.y, OwnerTeamId = c.OwnerTeamId, OccupyProgress = c.OccupyProgress }; }
        private static MessageOfCharacter BuildCharacterMessage(TrialCharacter c) { Vector2 p = Tool.GridToGame(c.Row, c.Col); return new MessageOfCharacter { Guid = c.Guid, TeamId = c.TeamId, PlayerId = c.PlayerId, CharacterType = CharacterType.Robot, CharacterActiveState = c.State, X = (int)p.x, Y = (int)p.y, Speed = CharacterSpeed, ViewRange = 5000, CommonAttack = CharacterAttack, CommonAttackRange = CharacterAttackRange, Hp = c.Hp, CarryCapacity = CharacterLoad, CurrentLoad = 0, HarvestRatePerSec = CharacterLoad }; }

        private void HandleKeyboard()
        {
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) MoveBy(-1, 0);
            else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) MoveBy(1, 0);
            else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) MoveBy(0, -1);
            else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) MoveBy(0, 1);
            else if (Input.GetKeyDown(KeyCode.H)) Harvest();
            else if (Input.GetKeyDown(KeyCode.O)) Occupy();
            else if (Input.GetKeyDown(KeyCode.P)) Produce();
            else if (Input.GetKeyDown(KeyCode.U)) UpgradeTech();
        }

        private void MoveToMouseCell()
        {
            Camera cam = Camera.main;
            if (cam == null || character == null) return;
            Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
            int row = Mathf.Clamp(MapRows - Mathf.FloorToInt(world.y) - 1, 0, MapRows - 1);
            int col = Mathf.Clamp(Mathf.FloorToInt(world.x), 0, MapCols - 1);
            if (!IsBlocked(row, col)) { character.Row = row; character.Col = col; character.State = CharacterState.Moving; StatusText = string.Format("试玩：右键移动到 ({0}, {1})", row, col); SubmitFrame(); }
        }

        private TrialResource FindNearestResource(){ TrialResource best=null; int bestDist=int.MaxValue; foreach(TrialResource r in resources.Values){ if(r.RemainingAmount<=0)continue; int dist=Math.Abs(r.Row-character.Row)+Math.Abs(r.Col-character.Col); if(dist<bestDist){best=r;bestDist=dist;}} return best; }
        private TrialComputeCenter FindNearestComputeCenter(){ TrialComputeCenter best=null; int bestDist=int.MaxValue; foreach(TrialComputeCenter c in computeCenters.Values){ int dist=Math.Abs(c.Row-character.Row)+Math.Abs(c.Col-character.Col); if(dist<bestDist){best=c;bestDist=dist;}} return best; }
        private bool IsBlocked(int row, int col) { return OfficialMap[row][col] == (int)PlaceType.Barrier; }
        private static bool IsPointerOverUi() { return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(); }
        private void SetCharacterState(CharacterState state, string status) { if (character != null) character.State = state; StatusText = status; SubmitFrame(); }

        [Serializable]
        private sealed class TrialOptions
        {
            public long teamId = 1;
            public long characterPlayerId = 1;
            public int sideFlag = 1;
        }

        private sealed class TrialTeam { public long TeamId; public int Score; public int Material; public int ComputePower; public readonly Dictionary<string, int> TechLevels = new Dictionary<string, int>(); }
        private sealed class TrialFactory { public long FactoryId; public long TeamId; public int Row; public int Col; public int Hp; }
        private sealed class TrialResource { public long Id; public int Row; public int Col; public ResourceType Type; public ResourceState State; public int RemainingAmount; public int MaxAmount; }
        private sealed class TrialComputeCenter { public long Id; public int Row; public int Col; public long OwnerTeamId; public int OccupyProgress; }
        private sealed class TrialCharacter { public long Guid; public long TeamId; public long PlayerId; public int Row; public int Col; public int Hp; public CharacterState State; }
    }
}
