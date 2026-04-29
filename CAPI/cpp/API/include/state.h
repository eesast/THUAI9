#pragma once
#ifndef STATE_H
#define STATE_H

#include <vector>
#include <array>
#include <map>
#include <memory>

#include "structures.h"

#undef GetMessage
#undef SendMessage
#undef PeekMessage

// 存储场上的状态
struct State
{
    std::shared_ptr<THUAI9::Character> characterSelf;
    std::shared_ptr<THUAI9::Team> teamSelf;
    std::vector<std::shared_ptr<THUAI9::Character>> characters;
    std::vector<std::shared_ptr<THUAI9::Character>> enemyCharacters;
    std::vector<std::vector<THUAI9::PlaceType>> gameMap;
    std::shared_ptr<THUAI9::GameMap> mapInfo;
    std::shared_ptr<THUAI9::GameInfo> gameInfo;
    std::vector<int64_t> guids;
    std::vector<int64_t> allGuids;
};

#endif