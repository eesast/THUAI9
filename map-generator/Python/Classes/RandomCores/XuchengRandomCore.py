from __future__ import annotations
from math import floor
from random import random

from easygui import multenterbox

from GameClass.MapGenerator import MapStruct
from Preparation.Utility import PlaceType as PT
from Classes.RandomCore import RandomCore


class DefaultXuchengRandomSettings:
    resourceNum = 7
    computeCenterNum = 3
    marketNum = 3
    bushProb = 0.015
    bushCrossBonus = 23
    barrierProb = 0.01
    barrierCrossBonus = 40


class XuchengRandomCore(RandomCore):
    title: str
    resourceNum: int
    computeCenterNum: int
    marketNum: int
    bushProb: float
    bushCrossBonus: int
    barrierProb: float
    barrierCrossBonus: int

    @property
    def ResourceNum(self) -> int:
        return self.resourceNum

    @ResourceNum.setter
    def ResourceNum(self, value: int) -> None:
        if value < 1 or value > 10:
            self.resourceNum = DefaultXuchengRandomSettings.resourceNum
        else:
            self.resourceNum = value

    @property
    def ComputeCenterNum(self) -> int:
        return self.computeCenterNum

    @ComputeCenterNum.setter
    def ComputeCenterNum(self, value: int) -> None:
        if value < 1 or value > 10:
            self.computeCenterNum = DefaultXuchengRandomSettings.computeCenterNum
        else:
            self.computeCenterNum = value

    @property
    def MarketNum(self) -> int:
        return self.marketNum

    @MarketNum.setter
    def MarketNum(self, value: int) -> None:
        if value < 1 or value > 10:
            self.marketNum = DefaultXuchengRandomSettings.marketNum
        else:
            self.marketNum = value

    @property
    def BushProb(self) -> float:
        return self.bushProb

    @BushProb.setter
    def BushProb(self, value: float) -> None:
        if value < 0 or value > 0.1:
            self.bushProb = DefaultXuchengRandomSettings.bushProb
        else:
            self.bushProb = value

    @property
    def BushCrossBonus(self) -> int:
        return self.bushCrossBonus

    @BushCrossBonus.setter
    def BushCrossBonus(self, value: int) -> None:
        if value < 1 or value > 50:
            self.bushCrossBonus = DefaultXuchengRandomSettings.bushCrossBonus
        else:
            self.bushCrossBonus = value

    @property
    def BarrierProb(self) -> float:
        return self.barrierProb

    @BarrierProb.setter
    def BarrierProb(self, value: float) -> None:
        if value < 0 or value > 0.1:
            self.barrierProb = DefaultXuchengRandomSettings.barrierProb
        else:
            self.barrierProb = value

    @property
    def BarrierCrossBonus(self) -> int:
        return self.barrierCrossBonus

    @BarrierCrossBonus.setter
    def BarrierCrossBonus(self, value: int) -> None:
        if value < 1 or value > 50:
            self.barrierCrossBonus = DefaultXuchengRandomSettings.barrierCrossBonus
        else:
            self.barrierCrossBonus = value

    def __init__(self,
                 title,
                 resourceNum: int = DefaultXuchengRandomSettings.resourceNum,
                 computeCenterNum: int = DefaultXuchengRandomSettings.computeCenterNum,
                 marketNum: int = DefaultXuchengRandomSettings.marketNum,
                 bushProb: float = DefaultXuchengRandomSettings.bushProb,
                 bushCrossBonus: int = DefaultXuchengRandomSettings.bushCrossBonus,
                 barrierProb: float = DefaultXuchengRandomSettings.barrierProb,
                 barrierCrossBonus: int = DefaultXuchengRandomSettings.barrierCrossBonus) -> None:
        self.title = title
        self.ResourceNum = resourceNum
        self.ComputeCenterNum = computeCenterNum
        self.MarketNum = marketNum
        self.BushProb = bushProb
        self.BushCrossBonus = bushCrossBonus
        self.BarrierProb = barrierProb
        self.BarrierCrossBonus = barrierCrossBonus

    @property
    def Name(self) -> str:
        return 'Xucheng'

    def Menu(self) -> bool:
        try:
            (self.ResourceNum,
             self.ComputeCenterNum,
             self.MarketNum,
             self.BushProb,
             self.BushCrossBonus,
             self.BarrierProb,
             self.BarrierCrossBonus) = (lambda i1, i2, i3, f4, i5, f6, i7:
                                          (int(i1), int(i2), int(i3), float(f4), int(i5), float(f6), int(i7)))(*multenterbox(
                                              msg='Random settings',
                                              title=self.title,
                                              fields=[
                                                  'Resource 数量',
                                                  'ComputeCenter 数量',
                                                  'Market 数量',
                                                  'Bush 生成概率',
                                                  'Bush 蔓延加成',
                                                  'Barrier 生成概率',
                                                  'Barrier 蔓延加成'
                                              ],
                                              values=[self.ResourceNum,
                                                      self.ComputeCenterNum,
                                                      self.MarketNum,
                                                      self.BushProb,
                                                      self.BushCrossBonus,
                                                      self.BarrierProb,
                                                      self.BarrierCrossBonus]
                                          ))
        except TypeError:
            return False
        return True

    def Random(self, mp: MapStruct) -> None:
        mp.Clear()
        XuchengRandomCore.generateBorderBarrier(mp)
        XuchengRandomCore.generateFactory(mp)
        XuchengRandomCore.generateResource(mp, self.resourceNum)
        XuchengRandomCore.generateComputeCenter(mp, self.computeCenterNum)
        XuchengRandomCore.generateMarket(mp, self.marketNum)
        XuchengRandomCore.generateBush(mp, self.bushProb, self.bushCrossBonus)
        XuchengRandomCore.generateBarrier(mp, self.barrierProb, self.barrierCrossBonus)

    @staticmethod
    def isEmptyNearby(mp: MapStruct, x: int, y: int, r: int) -> bool:
        for i in range(x - r if x - r >= 0 else 0, (x + r if x + r <= 49 else 49) + 1):
            for j in range(y - r if y - r >= 0 else 0, (y + r if y + r <= 49 else 49) + 1):
                if mp[i, j] != PT.NULL_PLACE_TYPE and mp[i, j] != PT.SPACE:
                    return False
        return True

    @staticmethod
    def haveSthNearby(mp: MapStruct, x: int, y: int, r: int, tp: PT) -> int:
        ret = 0
        for i in range(x - r if x - r >= 0 else 0, (x + r if x + r <= 49 else 49) + 1):
            for j in range(y - r if y - r >= 0 else 0, (y + r if y + r <= 49 else 49) + 1):
                if mp[i, j] == tp:
                    ret += 1
        return ret

    @staticmethod
    def haveSthCross(mp: MapStruct, x: int, y: int, r: int, tp: PT) -> int:
        ret = 0
        for i in range(x - r if x - r >= 0 else 0, (x + r if x + r <= 49 else 49) + 1):
            if mp[i, y] == tp:
                ret += 1
        for j in range(y - r if y - r >= 0 else 0, (y + r if y + r <= 49 else 49) + 1):
            if mp[x, j] == tp:
                ret += 1
        return ret

    @staticmethod
    def generateBorderBarrier(mp: MapStruct) -> None:
        for i in range(50):
            mp[i, 0] = PT.BARRIER
            mp[i, 49] = PT.BARRIER
            mp[0, i] = PT.BARRIER
            mp[49, i] = PT.BARRIER

    @staticmethod
    def generateFactory(mp: MapStruct) -> None:
        mp[3, 3] = PT.FACTORY
        mp[3, 46] = PT.FACTORY
        mp[46, 3] = PT.FACTORY
        mp[46, 46] = PT.FACTORY

    @staticmethod
    def generateResource(mp: MapStruct, num: int = DefaultXuchengRandomSettings.resourceNum) -> None:
        i = 0
        while i < num:
            x = floor(random() * 48) + 1
            y = floor(random() * 23) + 1
            if XuchengRandomCore.isEmptyNearby(mp, x, y, 2):
                mp[x, y] = PT.RESOURCE
                mp[49 - x, 49 - y] = PT.RESOURCE
            else:
                i -= 1
            i += 1

    @staticmethod
    def generateComputeCenter(mp: MapStruct, num: int = DefaultXuchengRandomSettings.computeCenterNum) -> None:
        i = 0
        while i < num:
            x = floor(random() * 48) + 1
            y = floor(random() * 23) + 1
            if XuchengRandomCore.isEmptyNearby(mp, x, y, 1):
                mp[x, y] = PT.COMPUTE_CENTER
                mp[49 - x, 49 - y] = PT.COMPUTE_CENTER
            else:
                i -= 1
            i += 1

    @staticmethod
    def generateMarket(mp: MapStruct, num: int = DefaultXuchengRandomSettings.marketNum) -> None:
        i = 0
        while i < num:
            x = floor(random() * 48) + 1
            y = floor(random() * 23) + 1
            if XuchengRandomCore.isEmptyNearby(mp, x, y, 1):
                mp[x, y] = PT.MARKET
                mp[49 - x, 49 - y] = PT.MARKET
            else:
                i -= 1
            i += 1

    @staticmethod
    def generateBush(mp: MapStruct, prob: float = DefaultXuchengRandomSettings.bushProb,
                     crossBonus: int = DefaultXuchengRandomSettings.bushCrossBonus) -> None:
        for i in range(50):
            for j in range(50):
                if ((mp[i, j] == PT.NULL_PLACE_TYPE or mp[i, j] == PT.SPACE) and
                        random() < prob * (XuchengRandomCore.haveSthCross(mp, i, j, 1, PT.BUSH) * crossBonus + 1)):
                    mp[i, j] = PT.BUSH
                    mp[49 - i, 49 - j] = PT.BUSH

    @staticmethod
    def generateBarrier(mp: MapStruct, prob: float = DefaultXuchengRandomSettings.barrierProb,
                        crossBonus: int = DefaultXuchengRandomSettings.barrierCrossBonus) -> None:
        for i in range(2, 48):
            for j in range(2, 48):
                if ((mp[i, j] == PT.NULL_PLACE_TYPE or mp[i, j] == PT.SPACE or mp[i, j] == PT.BUSH) and
                    not XuchengRandomCore.haveSthNearby(mp, i, j, 1, PT.FACTORY) and
                        random() < prob
                        * (XuchengRandomCore.haveSthCross(mp, i, j, 1, PT.BARRIER)
                           * (0 if XuchengRandomCore.haveSthCross(mp, i, j, 1, PT.BARRIER) > 1
                              else crossBonus) + 1)):
                    mp[i, j] = PT.BARRIER
                    mp[49 - i, 49 - j] = PT.BARRIER
