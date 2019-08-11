using Discord;
using Discord.Net;
using Discord.Commands;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;

namespace Firefly_One_Night_Ultimate_Werewolf
{
    class Execute
    {
        static void Main(string[] args)
        {
            new WerewolfController();
        }
    }
}
