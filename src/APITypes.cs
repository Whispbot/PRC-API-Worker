using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PRC_API_Worker
{
    public class PRC_Server
    {
        /// <summary>
        /// The name of the ER:LC server.
        /// </summary>
        public string name = "Server Name";

        /// <summary>
        /// The Roblox user ID of the server owner.
        /// </summary>
        public double ownerId = 1;

        /// <summary>
        /// The Roblox user ID's of the server co-owners.
        /// </summary>
        public List<double> coOwnerIds = [];

        /// <summary>
        /// The number of players currently in the server.
        /// </summary>
        public byte currentPlayers = 0;

        /// <summary>
        /// The maximum number of players allowed in the server.
        /// </summary>
        public byte maxPlayers = 40;

        /// <summary>
        /// The key used to join the server.
        /// </summary>
        public string joinKey = "";
        
        /// <summary>
        /// The account verification requirement for joining the server.
        /// </summary>
        public string accVerifiedReq = "Disabled";

        /// <summary>
        /// Whether team balance is enabled for the server.
        /// </summary>
        public bool teamBalance = true;
    }

    public class PRC_Player
    {
        /// <summary>
        /// The player's name in the format "{Username}:{UserId}".
        /// </summary>
        public string Player = "Roblox:1";

        /// <summary>
        /// The user's permission level in the server.
        /// Possible: Server Owner / Server Co-Owner / Server Administrator / Server Moderator / Normal
        /// </summary>
        public string Permission = "Normal";

        /// <summary>
        /// The player's callsign, if applicable.
        /// </summary>
        public string? Callsign = null;

        /// <summary>
        /// The team the player is currently on.
        /// </summary>
        public string Team = "Civilian";
    }

    public class PRC_JoinLog
    {
        /// <summary>
        /// Whether this is a join, if false, is leave.
        /// </summary>
        public bool Join = true;

        /// <summary>
        /// The timestamp of the join/leave in seconds since the Unix epoch.
        /// </summary>
        public double Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>
        /// The player's name in the format "{Username}:{UserId}".
        /// </summary>
        public string Player = "Roblox:1";
    }

    public class PRC_KillLog
    {
        /// <summary>
        /// The person who was killed in the format "{Username}:{UserId}".
        /// </summary>
        public string Killed = "Roblox:1";

        /// <summary>
        /// The person who killed in the format "{Username}:{UserId}".
        /// </summary>
        public string Killer = "Roblox:1";

        /// <summary>
        /// The timestamp of the kill in seconds since the Unix epoch.
        /// </summary>
        public double Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public class PRC_CommandLog
    {
        /// <summary>
        /// The player's name in the format "{Username}:{UserId}".
        /// </summary>
        public string Player = "Roblox:1";

        /// <summary>
        /// The timestamp of the command in seconds since the Unix epoch.
        /// </summary>
        public double Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>
        /// The command that was executed.
        /// </summary>
        public string Command = ":h Error Loading Data :(";
    }

    public class PRC_CallLog
    {
        /// <summary>
        /// The caller's name in the format "{Username}:{UserId}".
        /// </summary>
        public string Caller = "Roblox:1";

        /// <summary>
        /// The moderator's name in the format "{Username}:{UserId}".
        /// </summary>
        public string Moderator = "Roblox:1";

        /// <summary>
        /// The timestamp of the call in seconds since the Unix epoch.
        /// </summary>
        public double Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public class PRC_Vehicle
    {
        /// <summary>
        /// The texture on the vehicle, if applicable.
        /// </summary>
        public string? Texture = null;

        /// <summary>
        /// The name of the vehicle in the format "{Year} {Name}".
        /// </summary>
        public string Name = "2035 Vroom Vroom Car";

        /// <summary>
        /// The Roblox username of the vehicle's owner.
        /// </summary>
        public string Owner = "Roblox";
    }

    public class PRC_Staff
    {
        /// <summary>
        /// The Roblox user IDs of the server co-owners.
        /// </summary>
        public List<double> CoOwners = [];

        /// <summary>
        /// A dictionary of server administrators with the key as their Roblox user ID and the value as their username.
        /// </summary>
        public Dictionary<string, string> Admins = [];

        /// <summary>
        /// A dictionary of server moderators with the key as their Roblox user ID and the value as their username.
        /// </summary>
        public Dictionary<string, string> Mods = [];
    }
}
