from __future__ import annotations

import PyAPI.structures as THUAI9
from PyAPI.Interface import IAI, ICharacterAPI, ITeamAPI


class Setting:
    @staticmethod
    def Asynchronous() -> bool:
        return False


class AI(IAI):
    def __init__(self, playerID: int):
        self.playerID = playerID

        # 你可以在这里保存全局状态
        # 例如：阶段标记、路径缓存、目标点、消息记录等
        self._team_phase = 0
        self._character_phase = 0

    def CharacterPlay(self, api: ICharacterAPI) -> None:
        # 这里写“单个单位”的策略
        # 常用接口：
        # api.GetSelfInfo()         获取自己信息
        # api.GetFullMap()          获取完整地图
        # api.GetCharacters()       获取己方可见角色
        # api.GetEnemyCharacters()  获取敌方可见角色
        # api.GetResourceState(x, y) 获取资源点信息
        # api.GetMarketState(x, y)   获取市场信息
        # api.GetFactoryState(x, y)  获取工厂信息
        #
        # 常见动作接口：
        # api.Move(...) / api.MoveUp(...) / api.MoveDown(...)
        # api.Harvest() / api.Load(...) / api.Sell(...) / api.Buy(...)
        # api.Common_Attack(...)
        # api.EndAllAction()
        self_info = api.GetSelfInfo()
        if self_info is None:
            return

        # TODO:
        # 1. 读取当前位置、载重、血量、状态
        # 2. 根据当前阶段决定“去资源点 / 回工厂 / 去市场 / 追敌人”
        # 3. 用 if/elif 或状态机更新 self._character_phase
        # 4. 调用上面的动作接口执行你的策略
        del api

    def TeamPlay(self, api: ITeamAPI) -> None:
        # 这里写“队伍级”的策略
        # 常用接口：
        # api.GetSelfInfo()     获取本队信息
        # api.GetCharacters()    获取己方角色列表
        # api.GetGameInfo()      获取比赛时间和整体信息
        # api.GetMaterial()      获取工厂原料
        # api.GetScore()         获取当前分数
        #
        # 常见管理动作：
        # api.BuildCharacter(...)   造单位
        # api.ProduceGoods(...)     生产货物
        # api.UplevelTech(...)      升级科技
        team_info = api.GetSelfInfo()
        if team_info is None:
            return

        # TODO:
        # 1. 开局决定先造什么单位
        # 2. 根据原料、分数、科技状态决定是否生产货物或升级科技
        # 3. 用 self._team_phase 记录队伍处于哪个阶段
        # 4. 需要时可以给己方单位发消息协同
        del api
