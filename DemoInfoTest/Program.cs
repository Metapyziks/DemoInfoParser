﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using ProtoBuf;

namespace DemoInfoTest
{
    [ProtoContract]
    public class DemoInfoMessage
    {
        private static readonly DateTime Epoch = new DateTime( 1970, 1, 1, 0, 0, 0, DateTimeKind.Utc );

        [ProtoMember( 1 ), JsonIgnore]
        public ulong StartTimeSeconds64 { get; set; }

        [ProtoMember( 2 ), JsonIgnore]
        private uint StartTimeSeconds { get; set; }

        public DateTime StartTime
        {
            get
            {
                return Epoch.AddSeconds( StartTimeSeconds64 / (double) ((ulong) 1 << 31) );
            }
            set
            {
                var diff = (value - Epoch).TotalSeconds;
                StartTimeSeconds = (uint) diff;
                StartTimeSeconds64 = (ulong) diff * ((ulong) 1 << 31);
            }
        }

        [ProtoMember( 3 )]
        public MatchMessage Match { get; set; } = new MatchMessage();

        [ProtoMember( 4 ), JsonIgnore]
        private RoundMessage LastRound
        {
            get { return Rounds.Count == 1 ? Rounds[0] : null; }
            set { Rounds.Clear(); Rounds.Add( value ); }
        }

        [ProtoMember( 5 ), JsonIgnore]
        private List<RoundMessage> AllRounds
        {
            get { return Rounds.Count != 1 ? Rounds : null; }
            set { Rounds.Clear(); Rounds.AddRange( value ); }
        }
        
        public List<RoundMessage> Rounds { get; } = new List<RoundMessage>();
    }

    [ProtoContract]
    public class MatchMessage
    {
        [ProtoMember( 1 )]
        public int Server { get; set; }

        [ProtoMember( 2 )]
        public uint Id { get; set; }

        [ProtoMember( 3, IsRequired = true )]
        public int Unknown { get; set; }

        [ProtoMember( 7 )]
        public ulong Hash { get; set; }
    }

    [ProtoContract]
    public class RoundMessage
    {
        [ProtoMember( 1 )]
        public ulong? EndTime { get; set; }

        [ProtoMember( 2 )]
        public RoundHeaderMessage Header { get; set; }

        [ProtoMember( 5 )]
        public List<int> Kills { get; set; }

        [ProtoMember( 6 )]
        public List<int> Assists { get; set; }

        [ProtoMember( 7 )]
        public List<int> Deaths { get; set; }

        [ProtoMember( 8 )]
        public List<int> Score { get; set; }

        [ProtoMember( 11 )]
        public int? Winner { get; set; }

        [ProtoMember( 12 )]
        public List<int> TeamScore { get; set; }

        [ProtoMember( 15 )]
        public int ElapsedSeconds { get; set; }

        [ProtoMember( 16 )]
        public List<int> EnemyKills { get; set; }

        [ProtoMember( 17 )]
        public List<int> Headshots { get; set; }

        [ProtoMember( 21 )]
        public List<int> Mvps { get; set; }
    }

    [ProtoContract]
    public class RoundHeaderMessage
    {
        static string SteamIdToString( uint id )
        {
            const int universe = 0;
            return $"STEAM_{universe}:{id & 1}:{id >> 1}";
        }

        [ProtoMember( 1 ), JsonIgnore]
        public List<uint> SteamIds { get; set; } = new List<uint>();

        [JsonProperty("SteamIds")]
        public IEnumerable<string> SteamIdStrings => SteamIds.Select( SteamIdToString );

        [ProtoMember( 2 )]
        public Map? Map { get; set; }
    }

    public enum Map : long
    {
        DeDust      = 8 | (1 << 8),
        DeDust2     = 8 | (1 << 9),
        DeTrain     = 8 | (1 << 10),
        DeAztec     = 8 | (1 << 11),
        DeInferno   = 8 | (1 << 12),
        DeNuke      = 8 | (1 << 13),
        DeVertigo   = 8 | (1 << 14),
        DeMirage    = 8 | (1 << 15),
        CsOffice    = 8 | (1 << 16),
        CsItaly     = 8 | (1 << 17),
        CsAssault   = 8 | (1 << 18),
        CsMilitia   = 8 | (1 << 19),
        DeCache     = 8 | (1 << 20),
        DeSeason    = 8 | (1 << 21),
        DeLog       = 8 | (1 << 22),
        DeLite      = 8 | (1 << 23),
        CsInsertion = 8 | (1 << 24),
        DeZoo       = 8 | (1 << 25),
        DeSantorini = 8 | (1 << 26),
        CsAgency    = 8 | (1 << 27),
        DeOverpass  = 8 | (1 << 28),
        DeCbble     = 8 | (1 << 29),
        DeCanals    = 8 | (1 << 30),
    }

    class Program
    {
        static bool AreEqual( byte[] a, byte[] b )
        {
            return a.Length == b.Length && a.Select( (x, i) => x == b[i] ).All( x => x );
        }

        static void Main( string[] args )
        {
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

            //const string defaultDir = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\csgo\replays";
            var defaultDir = Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location );

            var dir = args.Length != 0 ? args[0] : defaultDir;
            foreach ( var demoInfoPath in Directory.GetFiles( dir, "*.info", SearchOption.TopDirectoryOnly ) )
            {
                var name = Path.GetFileNameWithoutExtension( demoInfoPath );

                try
                {
                    DemoInfoMessage info;
                    using ( var stream = File.Open( demoInfoPath, FileMode.Open, FileAccess.Read, FileShare.Read ) )
                    {
                        info = Serializer.Deserialize<DemoInfoMessage>( stream );
                    }

                    Console.WriteLine( $"{name}: {info.Match.Unknown:x2}, {info.Match.Hash:x16}" );

                    var outPath = $"{Path.GetFileNameWithoutExtension( demoInfoPath )}.txt";
                    File.WriteAllText( outPath, JsonConvert.SerializeObject( info, Formatting.Indented, settings ) );

#if DEBUG
                    using ( var outStream = new MemoryStream() )
                    {
                        Serializer.Serialize( outStream, info );

                        var src = File.ReadAllBytes( demoInfoPath );
                        var dst = outStream.ToArray();

                        //Debug.Assert( AreEqual( src, dst ) );
                    }
#endif
                }
                catch ( Exception e )
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine( name );
                    Console.WriteLine( e );
                    Console.ResetColor();
                }
            }
        }
    }
}