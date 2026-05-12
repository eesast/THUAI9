#include "AI.h"
#include "logic.h"
#include "structures.h"

#include <array>
#include <iostream>
#include <memory>
#include <string>
#include <string_view>

#undef GetMessage
#undef SendMessage
#undef PeekMessage

#ifdef _MSC_VER
#pragma warning(disable : 4996)
#endif

using namespace std::literals::string_view_literals;

static constexpr std::string_view welcomeString = R"welcome(
______________ ___  ____ ___  _____  .___ ________
\__    ___/   |   \|    |   \/  _  \ |   /   __   \
  |    | /    ~    \    |   /  /_\  \|   \____    /
  |    | \    Y    /    |  /    |    \   |  /    /
  |____|  \___|_  /|______/\____|__  /___| /____/
                \/                 \/
)welcome"sv;

namespace
{
    void PrintUsage()
    {
        std::cerr << "Usage: capi -t <teamID 1-4> -p <playerID 0-3> "
                  << "[-I <serverIP>] [-P <serverPort>] [-d] [-o] [-w]\n";
    }
}  // namespace

int THUAI9Main(int argc, char** argv, CreateAIFunc AIBuilder)
{
    int pID = -1;
    int tID = -1;
    int cTypeInt = -1;
    std::string sIP = "127.0.0.1";
    std::string sPort = "8888";
    bool file = false;
    bool print = false;
    bool warnOnly = false;

    auto requireValue = [&](int& index, std::string_view flag) -> std::string
    {
        if (index + 1 >= argc)
            throw std::runtime_error("Missing value for " + std::string(flag));
        ++index;
        return argv[index];
    };

    try
    {
        for (int i = 1; i < argc; ++i)
        {
            const std::string_view arg = argv[i];
            if (arg == "-I" || arg == "--serverIP")
                sIP = requireValue(i, arg);
            else if (arg == "-P" || arg == "--serverPort")
                sPort = requireValue(i, arg);
            else if (arg == "-t" || arg == "--teamID")
                tID = std::stoi(requireValue(i, arg));
            else if (arg == "-p" || arg == "--playerID")
                pID = std::stoi(requireValue(i, arg));
            else if (arg == "-c" || arg == "--characterType")
                cTypeInt = std::stoi(requireValue(i, arg));
            else if (arg == "-d" || arg == "--debug")
                file = true;
            else if (arg == "-o" || arg == "--output")
                print = true;
            else if (arg == "-w" || arg == "--warning")
                warnOnly = true;
            else
                throw std::runtime_error("Unknown argument: " + std::string(arg));
        }
    }
    catch (const std::exception& e)
    {
        std::cerr << e.what() << '\n';
        PrintUsage();
        return 1;
    }

    if (tID < 1 || tID > 4 || pID < 0)
    {
        PrintUsage();
        return 1;
    }

    if (!print)
        warnOnly = false;

    try
    {
        THUAI9::PlayerType playerType = pID == 0 ? THUAI9::PlayerType::Team : THUAI9::PlayerType::Character;
        THUAI9::CharacterType characterType = cTypeInt >= 0 ? static_cast<THUAI9::CharacterType>(cTypeInt) : THUAI9::CharacterType::NullCharacterType;

#ifdef _MSC_VER
        std::cout << welcomeString << std::endl;
#endif
        Logic logic(pID, tID, playerType, characterType);
        logic.Main(AIBuilder, sIP, sPort, file, print, warnOnly);
    }
    catch (const std::exception& e)
    {
        std::cerr << "C++ Exception: " << e.what() << '\n';
        return 1;
    }
    catch (...)
    {
        std::cerr << "Unknown Exception\n";
        return 1;
    }
    return 0;
}

std::unique_ptr<IAI> CreateAI(int32_t pID)
{
    return std::make_unique<AI>(pID);
}

int main(int argc, char* argv[])
{
    return THUAI9Main(argc, argv, CreateAI);
}
