from __future__ import annotations

from PyAPI.Interface import IAI, ICharacterAPI, ITeamAPI


class Setting:
    @staticmethod
    def Asynchronous() -> bool:
        return False


class AI(IAI):
    def __init__(self, playerID: int):
        self.playerID = playerID

    def CharacterPlay(self, api: ICharacterAPI) -> None:
        del api

    def TeamPlay(self, api: ITeamAPI) -> None:
        del api
