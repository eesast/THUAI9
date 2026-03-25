using Grpc.Core;
using Protobuf;

namespace ClientTest
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Please provide both CharacterId and TeamId as arguments.");
                return Task.CompletedTask;
            }
            if (!int.TryParse(args[0], out int playerId))
            {
                Console.WriteLine("Invalid CharacterId. Please provide a valid integer.");
                return Task.CompletedTask;
            }

            if (!int.TryParse(args[1], out int teamId))
            {
                Console.WriteLine("Invalid TeamId. Please provide a valid integer.");
                return Task.CompletedTask;
            }

            Thread.Sleep(3000);
            Channel channel = new("127.0.0.1:8888", ChannelCredentials.Insecure);
            var client = new AvailableService.AvailableServiceClient(channel);
            RegisterFactoryMsg playerInfo = new()
            {
                PlayerId = playerId,
                TeamId = teamId,
                SideFlag = teamId
            };
            var call = client.RegisterFactory(playerInfo);

            Thread.Sleep(3000);

            CreateCharacterMsg createMsg = new()
            {
                CharacterType = CharacterType.Robot,   // 无人机
                TeamId = teamId,
                PlayerId = 1,
            };

            var createRes = client.CreatCharacter(createMsg); // 当前可用接口
            if (!createRes.ActSuccess)
            {
                Console.WriteLine("CreateCharacter failed.");
                return Task.CompletedTask;
            }

            MoveMsg moveMsg = new()
            {
                PlayerId = 1,
                TeamId = teamId,
                TimeInMilliseconds = 100,
                Angle = 0
            };
            int tot = 0;
            while (call.ResponseStream.MoveNext().Result)
            {
                var currentGameInfo = call.ResponseStream.Current;
                if (currentGameInfo.GameState == GameState.GameStart) break;
            }
            while (true)
            {
                Thread.Sleep(50);

                MoveRes boolRes = client.Move(moveMsg);
                //if (boolRes.ActSuccess == false) break;
                tot++;
                if (tot % 10 == 0) moveMsg.Angle += 1;
            }
            return Task.CompletedTask;
        }
    }
}