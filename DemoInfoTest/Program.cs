using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DemoInfoTest
{
    class RoundData
    {
        public TimeSpan? GameTime;

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

    enum DataType : byte
    {
        SteamIdOrTimestamp = 0x08,
        Unknown10 = 0x10,
        RoundHeaderLength = 0x12,
        DemoUrl = 0x1a,
        RoundDataLength = 0x2a,
        Kills = 0x28,
        Assists = 0x30,
        Deaths = 0x38,
        Score = 0x40,
        Winner = 0x58,
        TeamScore = 0x60,
        GameTime = 0x78,
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
                if ( reader.ReadByte() != (byte) DataType.SteamIdOrTimestamp ) throw new Exception();
                info.StartTime = ReadTimeStamp( reader );
                if ( reader.ReadByte() != 0x10 ) throw new Exception();
                info.StartTime2 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc ).AddSeconds( ReadVarInt( reader ) );
                if ( reader.ReadByte() != 0x1a ) throw new Exception();
                var firstRoundOffset = reader.ReadByte();
                if ( reader.ReadByte() != 0x08 ) throw new Exception();
                info.ServerId = ReadVarInt( reader );
                if ( reader.ReadByte() != 0x10 ) throw new Exception();
                info.MatchId = ReadVarInt( reader );
                if ( reader.ReadByte() != 0x18 ) throw new Exception();
                info.Unknown0 = ReadVarInt( reader );
                if ( reader.ReadByte() != 0x38 ) throw new Exception();
                info.Hash = ReadVarInt( reader );

                while ( reader.BaseStream.Position < reader.BaseStream.Length )
                {
                    info.ReadRound( reader );
                }
            }

            return info;
        }

        public DateTime StartTime;
        public DateTime StartTime2;
        public long ServerId;
        public long Unknown0;
        public long Unknown1;
        public long MatchId;
        public long Hash; // Maybe?
        public DateTime EndTime;
        public string DemoUrl;
        public bool WasTie;
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
            return new DateTime( 1970, 1, 1, 0, 0, 0, DateTimeKind.Utc ).AddSeconds( ReadVarInt( reader ) / (double) ((ulong) 1 << 31) );
        }

        private void ReadRound( BinaryReader reader )
        {
            var round = new RoundData();
            Rounds.Add( round );

            DataType lastType = 0;
            int index = 0;

            long dataEnd = reader.BaseStream.Length;
            long headerEnd = 0;

            while ( reader.BaseStream.Position < dataEnd )
            {
                var type = (DataType) reader.ReadByte();
                if ( lastType != type )
                {
                    lastType = type;
                    index = 0;
                }

                byte count;
                switch ( type )
                {
                    case DataType.RoundDataLength:
                        var dataLength = ReadVarInt( reader );
                        dataEnd = reader.BaseStream.Position + dataLength;
                        break;
                    case DataType.SteamIdOrTimestamp:
                        // Not sure about this
                        if ( headerEnd == 0 )
                        {
                            EndTime = ReadTimeStamp( reader );
                            break;
                        }
                        round.Players[index++].Player = new SteamId( (uint) ReadVarInt( reader ), 1, 1, 1 );
                        break;
                    case DataType.Unknown10:
                        // Always 2 bits set?
                        Unknown1 = ReadVarInt( reader );
                        break;
                    case DataType.RoundHeaderLength:
                        var headerLength = ReadVarInt( reader );
                        headerEnd = reader.BaseStream.Position + headerLength;
                        break;
                    case DataType.DemoUrl:
                        var urlLength = ReadVarInt( reader );
                        DemoUrl = Encoding.ASCII.GetString( reader.ReadBytes( (int) urlLength ) );
                        break;
                    case DataType.Kills:
                        round.Players[index++].Kills = (int) ReadVarInt( reader );
                        break;
                    case DataType.Assists:
                        round.Players[index++].Assists = (int) ReadVarInt( reader );
                        break;
                    case DataType.Deaths:
                        round.Players[index++].Deaths = (int) ReadVarInt( reader );
                        break;
                    case DataType.Score:
                        round.Players[index++].Score = (int) ReadVarInt( reader );
                        break;
                    case DataType.TeamScore:
                        round.Teams[index++].Score = (int) ReadVarInt( reader );
                        break;
                    case DataType.Winner:
                        WasTie = reader.ReadByte() == 0;
                        break;
                    case DataType.GameTime:
                        round.GameTime = TimeSpan.FromSeconds( ReadVarInt( reader ) );
                        break;
                    case DataType.EnemyKills:
                        count = reader.ReadByte();
                        round.Players[index++].EnemyKills = (int) ReadInt( reader, count );
                        break;
                    case DataType.Headshots:
                        count = reader.ReadByte();
                        round.Players[index++].Headshots = (int) ReadInt( reader, count );
                        break;
                    case DataType.Mvps:
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
        static string ToBin( long value, int bits )
        {
            var str = "";
            for (var i = bits - 1; i >= 0; --i)
            {
                str += (value >> i) & 1;
            }
            return str;
        }

        static void Main( string[] args )
        {
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

            //const string defaultDir = @"C:\Users\James\Documents\GitHub\DemoInfoParser\Examples";
            const string defaultDir = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\csgo\replays";

            var dir = args.Length != 0 ? args[0] : defaultDir;
            foreach ( var demoInfoPath in Directory.GetFiles( dir, "*.info", SearchOption.TopDirectoryOnly ) )
            {
                var name = Path.GetFileNameWithoutExtension( demoInfoPath );

                try
                {
                    var info = DemoInfo.FromFile( demoInfoPath );
                    Console.WriteLine( $"{name}: {info.Unknown0}, {info.Hash:x16}, {ToBin(info.Unknown1, 32)}" );
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