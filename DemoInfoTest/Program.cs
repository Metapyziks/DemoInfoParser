using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DemoInfoTest
{
    class RoundData
    {
        public byte Unknown0;
        public byte Unknown1;

        public long Unknown5;
        public string Unknown6;

        public byte Unknown2;
        public byte Unknown3;
        public short Unknown4;

        public byte GameEnd;

        public struct TeamData
        {
            public int Score;
        }

        public struct PlayerData
        {
            public long PlayerId;
            public int Kills;
            public int Assists;
            public int Deaths;
            public int Score;
            public int EnemyKills;
            public byte Unknown0;
            public byte Unknown1;
        }

        public readonly TeamData[] Teams = new TeamData[2];
        public readonly PlayerData[] Players = new PlayerData[10];
    }

    enum RoundDataType : byte
    {
        PlayerId = 0x08,
        Unknown10 = 0x10,
        Kills = 0x28,
        Assists = 0x30,
        Deaths = 0x38,
        Score = 0x40,
        Winner = 0x58,
        TeamScore = 0x60,
        Unknown78 = 0x78,
        Unknown80 = 0x80,
        Unknown88 = 0x88,
        UnknownA8 = 0xA8
    }

    class DemoInfo
    {
        private static string ByteArrayToString(byte[] arr)
        {
            return string.Join(" ", arr.Select(x => x.ToString("x2")));
        }

        public static DemoInfo FromFile(string path)
        {
            var info = new DemoInfo();

            using (var reader = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                info.Unknown0 = ByteArrayToString(reader.ReadBytes(17));
                var firstRoundOffset = reader.ReadByte();
                info.Unknown1 = ByteArrayToString(reader.ReadBytes(firstRoundOffset));

                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    info.ReadRound(reader);
                }
            }

            return info;
        }

        public string Unknown0;
        public string Unknown1;
        public readonly List<RoundData> Rounds = new List<RoundData>();

        private long ReadVarInt(BinaryReader reader)
        {
            ulong val = 0;
            byte next;
            do
            {
                next = reader.ReadByte();
                val <<= 7;
                val |= (ulong)(next & 0x7f);
            } while ((next & 0x80) == 0x80);

            return (long)val;
        }

        private void ReadRound(BinaryReader reader)
        {
            Debug.Assert(reader.ReadByte() == 0x2a);

            var length = reader.ReadByte();
            var end = reader.BaseStream.Position + length;

            var round = new RoundData();
            Rounds.Add(round);

            round.Unknown0 = reader.ReadByte();
            round.Unknown1 = reader.ReadByte();

            switch (round.Unknown1)
            {
                case 0x08:
                    round.Unknown6 = ByteArrayToString(reader.ReadBytes(10));
                    break;
                case 0x12:
                    break;
                default:
                    throw new NotImplementedException(round.Unknown1.ToString("x2"));
            }

            var headerLength = reader.ReadByte();

            RoundDataType lastType = 0;
            int index = 0;
            while (reader.BaseStream.Position < end)
            {
                var type = (RoundDataType)reader.ReadByte();
                if (lastType != type)
                {
                    lastType = type;
                    index = 0;
                }

                byte unknown;
                switch (type)
                {
                    case RoundDataType.PlayerId:
                        round.Players[index++].PlayerId = ReadVarInt(reader);
                        break;
                    case RoundDataType.Unknown10:
                        round.Unknown5 = ReadVarInt(reader);
                        break;
                    case RoundDataType.Kills:
                        round.Players[index++].Kills = (int) ReadVarInt( reader );
                        break;
                    case RoundDataType.Assists:
                        round.Players[index++].Assists = (int)ReadVarInt(reader);
                        break;
                    case RoundDataType.Deaths:
                        round.Players[index++].Deaths = (int)ReadVarInt(reader);
                        break;
                    case RoundDataType.Score:
                        round.Players[index++].Score = (int)ReadVarInt(reader);
                        break;
                    case RoundDataType.TeamScore:
                        round.Teams[index++].Score = (int)ReadVarInt(reader);
                        break;
                    case RoundDataType.Winner:
                        round.GameEnd = reader.ReadByte();
                        break;
                    case RoundDataType.Unknown78:
                        var leading = round.Unknown2 = reader.ReadByte();
                        if ((leading & 0x80) == 0x80)
                        {
                            round.Unknown3 = reader.ReadByte();
                        }
                        break;
                    case RoundDataType.Unknown80:
                        unknown = reader.ReadByte();
                        Debug.Assert(unknown == 1);
                        round.Players[index++].EnemyKills = (int)ReadVarInt(reader);
                        break;
                    case RoundDataType.Unknown88:
                        unknown = reader.ReadByte();
                        Debug.Assert(unknown == 1);
                        round.Players[index++].Unknown0 = reader.ReadByte();
                        break;
                    case RoundDataType.UnknownA8:
                        unknown = reader.ReadByte();
                        Debug.Assert(unknown == 1);
                        round.Players[index++].Unknown1 = reader.ReadByte();
                        break;
                    default:
                        throw new NotImplementedException(((byte)type).ToString("x2"));
                }
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var dir = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\csgo\replays";
            foreach (var demoInfoPath in Directory.GetFiles(dir, "*.info", SearchOption.TopDirectoryOnly))
            {
                Console.WriteLine(demoInfoPath);
                var info = DemoInfo.FromFile(demoInfoPath);
                var outPath = $"{Path.GetFileNameWithoutExtension(demoInfoPath)}.txt";
                File.WriteAllText(outPath, JsonConvert.SerializeObject(info, Formatting.Indented));
            }
        }
    }
}