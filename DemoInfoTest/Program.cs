using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DemoInfoTest
{
    class RoundData
    {
        public string Unknown0;
        public byte? Unknown1;

        public long? Unknown5;

        public TimeSpan? GameTime;

        public bool? WasTie;

        public string DemoUrl;

        public struct TeamData
        {
            public int? Score;
        }

        public struct PlayerData
        {
            public SteamId? Player;

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

    enum SteamIdFormat
    {
        V1,
        V3
    }

    struct SteamId
    {
        private static readonly Regex _sOldFormatRegex = new Regex( @"^STEAM_(?<universe>[0-5]):(?<bit>[0-1]):(?<id>[0-9]+)$" );
        private static readonly Regex _sNewFormatRegex = new Regex( @"^(?<type>[U]):(?<universe>[0-5]):(?<id>[0-9]+)$" );

        public static explicit operator ulong( SteamId steamId )
        {
            return steamId.Value;
        }

        public static SteamId Parse( string steamId )
        {
            uint id;
            int instance;
            byte type;
            byte universe;

            Match match;
            if ( (match = _sOldFormatRegex.Match( steamId )).Success )
            {
                id = (uint.Parse( match.Groups["id"].Value ) << 1) | uint.Parse( match.Groups["bit"].Value );
                instance = 1;
                type = 1;
                universe = byte.Parse( match.Groups["universe"].Value );
            }
            else if ( (match = _sNewFormatRegex.Match( steamId )).Success )
            {
                id = byte.Parse( match.Groups["id"].Value );
                instance = 1;
                type = 1;
                universe = byte.Parse( match.Groups["universe"].Value );
            }
            else
            {
                throw new Exception( "Invalid SteamID format." );
            }

            return new SteamId( id, instance, type, universe );
        }

        [JsonProperty( "SteamID64" )]
        public readonly ulong Value;

        [JsonProperty( "SteamID" )]
        public string V1 => ToString( SteamIdFormat.V1 );

        [JsonProperty( "SteamID3" )]
        public string V3 => ToString( SteamIdFormat.V3 );

        public SteamId( uint id, int instance, byte type, byte universe )
        {
            Value = id | ((ulong) instance << 32) | ((ulong) type << 52) | ((ulong) universe << 56);
        }

        public SteamId( ulong value )
        {
            Value = value;
        }

        private string ToString( SteamIdFormat format )
        {
            var id = Value & 0xffffffff;
            var instance = (Value >> 32) & 0xfffff;
            var type = (Value >> 52) & 0xf;
            var universe = (Value >> 56) & 0xff;

            switch ( format )
            {
                case SteamIdFormat.V1:
                    return $"STEAM_{universe}:{id & 1}:{id >> 1}";
                case SteamIdFormat.V3:
                    return $"U:{universe}:{id}";
                default:
                    throw new ArgumentException();
            }
        }

        public override string ToString()
        {
            return ToString( SteamIdFormat.V1 );
        }
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
        private static string ByteArrayToString( byte[] arr )
        {
            return string.Join( " ", arr.Select( x => x.ToString( "x2" ) ) );
        }

        public static DemoInfo FromFile( string path )
        {
            var info = new DemoInfo();

            using ( var reader = new BinaryReader( File.Open( path, FileMode.Open, FileAccess.Read, FileShare.Read ) ) )
            {
                if ( reader.ReadByte() != 0x08 ) throw new Exception();
                info.Unknown0 = ByteArrayToString( reader.ReadBytes( 2 ) );
                if ( reader.ReadByte() != 0x80 ) throw new Exception();
                if ( reader.ReadByte() != 0x80 ) throw new Exception();
                info.StartTime = ReadTimeStamp( reader );
                if ( reader.ReadByte() != 0x10 ) throw new Exception();
                info.StartTime2 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc ).AddSeconds( ReadVarInt( reader ) );
                if ( reader.ReadByte() != 0x1a ) throw new Exception();
                var firstRoundOffset = reader.ReadByte();
                if ( reader.ReadByte() != 0x08 ) throw new Exception();
                info.Unknown0 += " | " + ByteArrayToString( reader.ReadBytes( 1 ) );
                if ( reader.ReadByte() != 0x01 ) throw new Exception();
                if ( reader.ReadByte() != 0x10 ) throw new Exception();
                info.Unknown1 = ReadVarInt( reader );
                if ( reader.ReadByte() != 0x18 ) throw new Exception();
                info.Unknown0 += " | " + ByteArrayToString( reader.ReadBytes( 1 ) );
                if ( reader.ReadByte() != 0x38 ) throw new Exception();
                info.Unknown2 = ReadVarInt( reader );
                if ( reader.ReadByte() != (byte) RoundDataType.NewRound )
                {
                    throw new FormatException();
                }

                while ( reader.BaseStream.Position < reader.BaseStream.Length )
                {
                    info.ReadRound( reader );
                }
            }

            return info;
        }

        public DateTime StartTime;
        public DateTime StartTime2;
        public string Unknown0;
        public long Unknown1;
        public long Unknown2;
        public DateTime EndTime;
        public readonly List<RoundData> Rounds = new List<RoundData>();

        private static long ReadVarInt( BinaryReader reader )
        {
            ulong val = 0;
            var shift = 0;
            byte next;
            do
            {
                next = reader.ReadByte();
                val |= (ulong) (next & 0x7f) << shift;
                shift += 7;
            } while ( (next & 0x80) == 0x80 );

            return (long) val;
        }

        private static long ReadInt( BinaryReader reader, int bytes )
        {
            ulong val = 0;
            for ( var i = 0; i < bytes; ++i )
            {
                val <<= 8;
                val |= reader.ReadByte();
            }
            return (long) val;
        }

        private static DateTime ReadTimeStamp( BinaryReader reader )
        {
            return new DateTime( 1970, 1, 1, 0, 0, 0, DateTimeKind.Utc ).AddSeconds( ReadVarInt( reader ) / 8d );
        }

        private void ReadRound( BinaryReader reader )
        {
            var length = reader.ReadByte();
            var round = new RoundData();
            Rounds.Add( round );

            var always1 = reader.ReadByte();
            if ( always1 != 1 )
            {
                throw new Exception( "Expected to read 0x01" );
            }

            // Always 8 or 18?
            var gameState = reader.ReadByte();

            switch ( gameState )
            {
                case 0x08:
                    round.Unknown0 = ByteArrayToString( reader.ReadBytes( 4 ) );
                    EndTime = ReadTimeStamp( reader );
                    round.Unknown1 = reader.ReadByte();
                    break;
                case 0x12:
                    break;
                default:
                    throw new NotImplementedException( gameState.ToString( "x2" ) );
            }

            var headerLength = reader.ReadByte();

            RoundDataType lastType = 0;
            int index = 0;
            while ( reader.BaseStream.Position < reader.BaseStream.Length )
            {
                var type = (RoundDataType) reader.ReadByte();
                if ( lastType != type )
                {
                    lastType = type;
                    index = 0;
                }

                byte count;
                switch ( type )
                {
                    case RoundDataType.NewRound:
                        return;
                    case RoundDataType.PlayerId:
                        round.Players[index++].Player = new SteamId( (uint) ReadVarInt( reader ), 1, 1, 1 );
                        break;
                    case RoundDataType.Unknown10:
                        // Always 2 bits set?
                        round.Unknown5 = ReadVarInt( reader );
                        break;
                    case RoundDataType.DemoUrl:
                        var urlLength = ReadVarInt( reader );
                        round.DemoUrl = Encoding.ASCII.GetString( reader.ReadBytes( (int) urlLength ) );
                        break;
                    case RoundDataType.Kills:
                        round.Players[index++].Kills = (int) ReadVarInt( reader );
                        break;
                    case RoundDataType.Assists:
                        round.Players[index++].Assists = (int) ReadVarInt( reader );
                        break;
                    case RoundDataType.Deaths:
                        round.Players[index++].Deaths = (int) ReadVarInt( reader );
                        break;
                    case RoundDataType.Score:
                        round.Players[index++].Score = (int) ReadVarInt( reader );
                        break;
                    case RoundDataType.TeamScore:
                        round.Teams[index++].Score = (int) ReadVarInt( reader );
                        break;
                    case RoundDataType.Winner:
                        round.WasTie = reader.ReadByte() == 0;
                        break;
                    case RoundDataType.Unknown78:
                        round.GameTime = TimeSpan.FromSeconds( ReadVarInt( reader ) );
                        break;
                    case RoundDataType.EnemyKills:
                        count = reader.ReadByte();
                        round.Players[index++].EnemyKills = (int) ReadInt( reader, count );
                        break;
                    case RoundDataType.Headshots:
                        count = reader.ReadByte();
                        round.Players[index++].Headshots = (int) ReadInt( reader, count );
                        break;
                    case RoundDataType.Mvps:
                        count = reader.ReadByte();
                        round.Players[index++].Mvps = (int) ReadInt( reader, count );
                        break;
                    default:
                        throw new NotImplementedException( ((byte) type).ToString( "x2" ) );
                }
            }
        }
    }

    class Program
    {
        static void Main( string[] args )
        {
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

            //const string defaultDir = @"C:\Users\James\Documents\GitHub\DemoInfoParser\Examples";
            const string defaultDir = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\csgo\replays";

            var dir = args.Length != 0 ? args[0] : defaultDir;
            foreach ( var demoInfoPath in Directory.GetFiles( dir, "*.info", SearchOption.TopDirectoryOnly ) )
            {
                try
                {
                    var info = DemoInfo.FromFile( demoInfoPath );
                    Console.WriteLine( $"{info.Unknown0}, {info.Unknown1:x8}, {info.Unknown2:x16}, {info.Rounds.Last().Unknown5:x8}" );
                    var outPath = $"{Path.GetFileNameWithoutExtension( demoInfoPath )}.txt";
                    File.WriteAllText( outPath, JsonConvert.SerializeObject( info, Formatting.Indented, settings ) );
                }
                catch ( Exception e )
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine( e );
                    Console.ResetColor();
                }
            }

            Console.ReadKey();
        }
    }
}