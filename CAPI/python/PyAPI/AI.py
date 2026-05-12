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

        # 可以在这里保存全局状态

        self._team_phase = 0
        self._character_phase = 0

    def CharacterPlay(self, api: ICharacterAPI) -> None:
        # 这里写“单个单位”的策略
        # 常用接口：
        # api.GetSelfInfo() 
        # api.GetFullMap()
        # api.GetCharacters()
        # api.GetEnemyCharacters()
        # api.GetResourceState(x, y)
        # api.GetMarketState(x, y)
        # api.GetFactoryState(x, y)


        self_info = api.GetSelfInfo()
        if self_info is None:
            return

        # TODO:

        del api

    def TeamPlay(self, api: ITeamAPI) -> None:
        # 这里写“队伍级”的策略
        # 常用接口：
        # api.GetSelfInfo()
        # api.GetCharacters()
        # api.GetGameInfo()
        # api.GetMaterial()
        # api.GetScore()
        # team_info.techLevels

        team_info = api.GetSelfInfo()
        if team_info is None:
            return

        # TODO:

        del api
