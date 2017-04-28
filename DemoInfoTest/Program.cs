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
        public byte? Unknown0;
        public byte? Unknown1;

        public long? Unknown5;
        public string Unknown6;

        public byte? Unknown2;
        public byte? Unknown3;
        public short? Unknown4;

        public byte? GameEnd;

        public string DemoUrl;

        public struct TeamData
        {
            public int? Score;
        }

        public struct PlayerData
        {
            public long? PlayerId;
            public int? Kills;
            public int? Assists;
            public int? Deaths;
            public int? Score;
            public int? EnemyKills;
            public int? Headshots;
            public int? Mvps;
        }

        public readonly TeamData[] Teams = new TeamData[2];
        public readonly PlayerData[] Players = new PlayerData[10];
    }

    enum RoundDataType : byte
    {
        PlayerId = 0x08,
        Unknown10 = 0x10,
        DemoUrl = 0x1a,
        NewRound = 0x2a,
        Kills = 0x28,
        Assists = 0x30,
        Deaths = 0x38,
        Score = 0x40,
        Winner = 0x58,
        TeamScore = 0x60,
        Unknown78 = 0x78,
        EnemyKills = 0x80,
        Headshots = 0x88,
        Mvps = 0xA8
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
                if ( reader.ReadByte() != (byte) RoundDataType.NewRound )
                {
                    throw new FormatException();
                }

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

        private long ReadInt( BinaryReader reader, int bytes )
        {
            ulong val = 0;
            for ( var i = 0; i < bytes; ++i )
            {
                val <<= 8;
                val |= reader.ReadByte();
            }
            return (long) val;
        }

        private void ReadRound(BinaryReader reader)
        {
            var length = reader.ReadByte();
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
                    throw new NotImplementedException(round.Unknown1?.ToString("x2"));
            }

            var headerLength = reader.ReadByte();

            RoundDataType lastType = 0;
            int index = 0;
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var type = (RoundDataType)reader.ReadByte();
                if (lastType != type)
                {
                    lastType = type;
                    index = 0;
                }

                byte count;
                switch (type)
                {
                    case RoundDataType.NewRound:
                        return;
                    case RoundDataType.PlayerId:
                        round.Players[index++].PlayerId = ReadVarInt(reader);
                        break;
                    case RoundDataType.Unknown10:
                        round.Unknown5 = ReadVarInt(reader);
                        break;
                    case RoundDataType.DemoUrl:
                        var urlLength = ReadVarInt( reader );
                        round.DemoUrl = Encoding.ASCII.GetString( reader.ReadBytes( (int) urlLength ) );
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
                    case RoundDataType.EnemyKills:
                        count = reader.ReadByte();
                        round.Players[index++].EnemyKills = (int) ReadInt( reader, count );
                        break;
                    case RoundDataType.Headshots:
                        count = reader.ReadByte();
                        round.Players[index++].Headshots = (int)ReadInt(reader, count);
                        break;
                    case RoundDataType.Mvps:
                        count = reader.ReadByte();
                        round.Players[index++].Mvps = (int)ReadInt(reader, count);
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
            var settings = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};

            var dir = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\csgo\replays";
            foreach (var demoInfoPath in Directory.GetFiles(dir, "*.info", SearchOption.TopDirectoryOnly))
            {
                Console.WriteLine(demoInfoPath);
                try
                {
                    var info = DemoInfo.FromFile( demoInfoPath );
                    var outPath = $"{Path.GetFileNameWithoutExtension( demoInfoPath )}.txt";
                    File.WriteAllText( outPath, JsonConvert.SerializeObject( info, Formatting.Indented, settings ) );
                }
                catch ( Exception e )
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(e);
                    Console.ResetColor();
                }
            }
        }
    }
}