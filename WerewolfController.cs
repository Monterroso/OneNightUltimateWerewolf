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
    public class WerewolfController
    {
        DiscordClient discord;
        CommandService commands;
        Dictionary<Channel, WerewolfGame> games;
        Dictionary<ulong, WerewolfGame> players;

        public WerewolfController()
        {
            discord = new DiscordClient(x =>
            {
                x.LogLevel = LogSeverity.Info;
                x.LogHandler = Log;
            });

            discord.UsingCommands(x =>
            {
                x.PrefixChar = '/';
                x.AllowMentionPrefix = true;
            });

            games = new Dictionary<Channel, WerewolfGame>();
            players = new Dictionary<ulong, WerewolfGame>();

            commands = discord.GetService<CommandService>();

            //do stuff when anyone sends a message to the bot
            //we don't want the bot to ever respond to itself
            discord.MessageReceived += (s, e) =>
            {
                //check if the bot is registering itself
                if(e.User.IsBot)
                {
                    return;
                }              
                AddPlayer(e);
                StartGame(e);
                RemovePlayer(e);
                KillGame(e);
                RegisterCommands(e);
                InitiateGame(e);
                Vote(e);
                Execute(e);
                Perform(e);
                //ModerateServerRoles(e);
            };

            discord.ExecuteAndWait(async () =>
            {
                await discord.Connect("Mjk2ODI2NDU5NzQ0MzcwNjkx.C734uA.i6JeWNocDClTqakqMKPsIM1EEbo", TokenType.Bot);
            });
        }

        /// <summary>
        /// Purpose of this function is to make sure all the players have proper roles once a game has started
        /// </summary>
        /// <param name="newUser"></param>
        private async Task ModerateServerRoles(MessageEventArgs e)
        {

            Channel channel = e.Channel;
            //if a werewolf game doesn't exist in the channel, this should do nothing
            if (!games.ContainsKey(channel))
            {
                return;
            }

            //if the game is going to be killed, then lets just ignore it
            if (CheckCommand(e, "/Kill"))
            {
                return;
            }

            //we run the block. If not everything has been executed, 
            //then we run the block again ad infinitum
            bool completed = false;

            while (!completed)
            {
                completed = true;
                List<Role> removeRoles = new List<Role>();
                List<Role> addRoles = new List<Role>();
                //if any of the players have the player role before the game has started, remove them
                foreach (User user in channel.Users)
                {
                    //check if the user is apart of the game, if not, remove game roles from them, add non playing role
                    if (!games[channel].Players.Keys.Contains(user.Id))
                    {
                        Console.WriteLine(user.Name + "Is not in the game but has roles " + user.Roles.First() + " which is not " + games[channel].playerRole);
                        //block for removing game roles
                        if (user.HasRole(games[channel].playerRole))
                        {
                            Console.WriteLine(user.Name + " has role " + games[channel].playerRole);
                            removeRoles.Add(games[channel].playerRole);

                            //the task still had a role it needed to perform, thus has not completed
                            completed = false;
                        }

                        //block for adding nonplaying roles
                        if (!user.HasRole(games[channel].nonRole))
                        {
                            Console.WriteLine(user.Name + " does not have role " + games[channel].nonRole);
                            addRoles.Add(games[channel].nonRole);

                            //the task still had a role it needed to perform, thus has not completed
                            completed = false;
                        }


                    }
                    else //otherwise, we see if we need to add 
                    {
                        Console.WriteLine(user.Name + "Is in the game ");
                        //block for adding game roles
                        if (!user.HasRole(games[channel].playerRole))
                        {
                            removeRoles.Add(games[channel].playerRole);

                            //the task still had a role it needed to perform, thus has not completed
                            completed = false;
                        }

                        //block for removing nonplaying roles
                        if (user.HasRole(games[channel].nonRole))
                        {
                            addRoles.Add(games[channel].nonRole);

                            //the task still had a role it needed to perform, thus has not completed
                            completed = false;
                        }
                    }

                    Console.WriteLine(user.Name + " has roles to add: ");
                    foreach (Role a in addRoles)
                    {
                        Console.WriteLine(a);
                    }
                    await user.RemoveRoles(removeRoles.ToArray());


                    Console.WriteLine(user.Name + " has roles to remove: ");
                    foreach (Role a in removeRoles)
                    {
                        Console.WriteLine(a);
                    }
                    await user.AddRoles(addRoles.ToArray());

                    addRoles = new List<Role>();
                    removeRoles = new List<Role>();

                }
            }
            Console.WriteLine("The moderate function has finished");
        }

        private void Log(object sender, LogMessageEventArgs e)
        {
            Console.WriteLine(e.Message);
        }

        /// <summary>
        /// checks to see if if the first word of the message is the specified message
        /// </summary>
        /// <param name="e"></param>
        /// <param name="message">word to be compared to</param>
        /// <returns></returns>
        private bool CheckCommand(MessageEventArgs e, String message)
        {
            String[] Tokens = e.Message.Text.Split(' ');

            if (Tokens.Length > 0)
            {
                if (Tokens[0].ToLower() == message.ToLower())
                {
                    return true;
                }

                return false;
            }

            Console.WriteLine("A blank message has been sent");

            return false;
        }

        private async void InitiateGame(MessageEventArgs e)
        {
            if (!CheckCommand(e, "/Init"))
            {
                return;
            }

            if (games.ContainsKey(e.Channel))
            {
                await e.Channel.SendMessage("There is already a game within this channel!");
                return;
            }

            if (!e.User.ServerPermissions.Administrator)
            {
                await e.Channel.SendMessage("You are not an administrator, therefore you cannot create a game!");
                return;
            }

            //now we want to check to see if there are proper roles for individuals to play the game
            String[] Tokens = e.Message.Text.Split(' ');

            if(Tokens.Length == 3 || Tokens.Length == 4)
            {
                Role plRole = null;
                Role noRole = null;

                foreach (Role role in e.Server.Roles)
                {
                    if (role.Name == Tokens[1])
                    {
                        plRole = role;
                    }
                    if (role.Name == Tokens[2])
                    {
                        noRole = role;
                    }
                }

                if (plRole == null || noRole == null)
                {
                    await e.Channel.SendMessage("A specific role was not found, please create the player and admin role, then try again!");
                    return;
                }
                bool debug = false;

                if (Tokens.Length == 4)
                {
                    if (Tokens[3] == "1")
                    {
                        debug = true;
                    }
                }

                games.Add(e.Channel, new WerewolfGame(e.Channel, plRole, noRole, debug));

                await e.Channel.SendMessage("A new Werewolf game is being created, please be patient");

                if (debug == true)
                {
                    await e.Channel.SendMessage("The game is in display mode, there will be no hidden information");
                }

                return;
            }

            await e.Channel.SendMessage("You have not specified the roles for the game!");
            

            
        }

        private async void StartGame(MessageEventArgs e)
        {

            if (!CheckCommand(e, "/Start"))
            {
                return;
            }

            if (!games.ContainsKey(e.Channel))
            {
                await e.Channel.SendMessage("A game must first be created before you can start one!");
                return;
            }

            if (!e.User.ServerPermissions.Administrator)
            {
                await e.Channel.SendMessage("You are not an administrator, therefore you cannot start a game!");
                return;
            }

            

            //now, we need to add all the player roles to the players
            foreach(ulong item in games[e.Channel].Players.Keys)
            {
                if (players.ContainsKey(item))
                {
                    await e.Channel.SendMessage(e.Channel.GetUser(item).Name + " is already in a game! They cannot partake in this one");
                    games[e.Channel].Players.Remove(item);
                }

                players.Add(item, games[e.Channel]);
            }

            String a = games[e.Channel].GenerateRoles();

            if (games[e.Channel].debug == true)
            {
                await e.Channel.SendMessage(a);
            }

            a = games[e.Channel].AssignRoles();

            if (games[e.Channel].debug == true)
            {
                await e.Channel.SendMessage(a);
            }

            await e.Channel.SendMessage("The game shall begin momentarily");

            MessageRoles(e);


            
        }

        private async void AddPlayer(MessageEventArgs e)
        {
            if (!CheckCommand(e, "/Join"))
            {
                return;
            }

            if(games.ContainsKey(e.Channel))
            {
                String a = games[e.Channel].AddPlayer(e.User.Id);
                await e.Channel.SendMessage(a);
               
            }
            else
            {
                await e.Channel.SendMessage("There is no game within this channel as of yet!");

            }
        }

        private async void RemovePlayer(MessageEventArgs e)
        {
            if (!CheckCommand(e, "/Leave"))
            {
                return;
            }

            if (games.ContainsKey(e.Channel))
            {
                String a = games[e.Channel].RemovePlayer(e.User.Id);
                await e.Channel.SendMessage(a);
            }
            else
            {
                await e.Channel.SendMessage("There is no game within this channel as of yet!");

            }
        }

        private void KillGame(MessageEventArgs e)
        {
            if (!CheckCommand(e, "/Kill"))
            {
                return;
            }

            if (e.User.ServerPermissions.Administrator == false)
            {
                e.Channel.SendMessage("You are not an administrator, therefore you cannot kill the game!");
                return;
            }
            games.Remove(e.Channel);
            e.Channel.SendMessage("The game has been removed");
        }

        private void MessageRoles(MessageEventArgs e)
        {
            e.Channel.SendMessage("Everyone playing shall have recieved their role!");
            foreach(KeyValuePair<ulong, WerewolfRole> item in games[e.Channel].Players)
            {
                e.Channel.GetUser(item.Key).SendMessage(item.Value.MessageRole());
            }
        }

        private void RegisterCommands(MessageEventArgs e)
        {
            if (!CheckCommand(e, "/Command"))
            {
                return;
            }
            e.User.SendMessage(players[e.User.Id].Players[e.User.Id].TakeInput(e.Message.Text));
        }

        private void Vote(MessageEventArgs e)
        {
            if (!CheckCommand(e, "/Vote"))
            {
                return;
            }
            e.User.SendMessage(players[e.User.Id].Vote(e.User, e.Message.Text));
        }

        private void Execute(MessageEventArgs e)
        {
            if (!CheckCommand(e, "/Execute"))
            {
                return;
            }

            if (!e.User.ServerPermissions.Administrator)
            {
                e.Channel.SendMessage("You are not an administrator, therefore you cannot tally the results!");
                return;
            }

            if (!games.ContainsKey(e.Channel))
            {
                e.Channel.SendMessage("A game must first be created before you can tally votes!");
                return;
            }

            e.Channel.SendMessage(games[e.Channel].ExecutePlayer());

        }

        private void Perform(MessageEventArgs e)
        {
            if (!CheckCommand(e, "/Perform"))
            {
                return;
            }

            if (!e.User.ServerPermissions.Administrator)
            {
                e.Channel.SendMessage("You are not an administrator, therefore you cannot cause the results to happen!");
                return;
            }

            if (!games.ContainsKey(e.Channel))
            {
                e.Channel.SendMessage("A game must first be created before the night can end!");
                return;
            }

            e.Channel.SendMessage(games[e.Channel].Perform());

        }
    }

    public abstract class WerewolfRole
    {
        //user that this role was assigned to. This may or may not be the current owner of the role;
        public User owner;

        //dictates in which order the role will be played
        public int order;

        //need to see if this role has already acted
        public bool acted; 

        //specifically this game
        public WerewolfGame game;

        public const String WerewolvesTeam = "You are on the Werewolves team, you win if neither a werewolf or tanner is killed";
        public const String VillagersTeam = "You are on the villagers team, you win if at least one wearwolf is killed.\n";
        public const String NoCommands = "You do not need any special commands for this role, your role will be handled automatically.\n";

        public const String SkipCommand = "abstain";

        public const String NoAction = "This role does not do anything during the night";

        public const String NoCommand = "This role does not does not require any commands";

        public const String CommandSuccess = "Your action has been successfully logged!";

        public const String PerformSuccess = "The action has been peformed";

        public const String AlreadyActed = "You have already performed your role for tonight!";

        public const String InvlaidWordCount = "There was an invalid number of words in your command";

        public String UsernotFound(string user)
        {
            return user + " was not found within the list of users within the channel";
        }

        public String GetPlayers<T>()
        {
            String SelectUser = "";

            foreach ( KeyValuePair<ulong,WerewolfRole> a in game.Players)
            {
                if (typeof(T).IsSubclassOf(a.Value.GetType()) || typeof(T) == a.Value.GetType())
                {
                    SelectUser += game.channel.GetUser(a.Key).Name + " ";
                }
            }

            return SelectUser;
        }

        //returns the message to be sent to the owner about their role and information
        public abstract String MessageRole();

        //function used to parse the command from the character
        public abstract String TakeInput(String command);

        //performs the actions for the character
        public abstract String PerformRole();
    }

    public class Observer : WerewolfRole
    {
        public Observer(WerewolfGame game, User owner)
        {
            this.game = game;
            this.owner = owner;
            this.order = -1;
            acted = true;
        }

        public override String MessageRole()
        {
            return "You are an observer to this game!";
        }

        public override string PerformRole()
        {
            return NoAction;
        }

        public override string TakeInput(string command)
        {
            return NoCommand;
        }
    }

    public class Villager : WerewolfRole
    {
        public Villager(WerewolfGame game, User owner)
        {
            this.game = game;
            this.owner = owner;
            this.order = -1;
            acted = true;
        }
        public override string MessageRole()
        {
            string message = "";

            message += "Villagers are simple individuals going about their lives, and have no special powers or abilities.\n";
            message += "Although this role might seem boring, it can actually be really fun! Villager is the defactor role Werewolves claim to be, so you might not be believed if you say you are a Villager, so be careful!\n";
            message += VillagersTeam;
            message += NoCommands;

            return message;
        }

        public override string PerformRole()
        {
            return NoCommand;
        }

        public override string TakeInput(String command)
        {
            return NoAction;
        }
    }

    public class Werewolf : WerewolfRole
    {
        public Werewolf(WerewolfGame game, User owner)
        {
            this.order = -1;
            this.game = game;
            this.owner = owner;
            acted = true;
        }

        public override String MessageRole()
        {
            string message = "";

            message += "You have been afflicted by a horrible curse. Every full moon, you transform into a Werewolf! Hopefully tonight isn't one...\n";
            message += "As a werewolf, you need to be sure that the Village doesn't find out your idenity or that of your kin!\n";
            message += WerewolvesTeam;
            message += NoCommands;
            message += "Other werewolves are: " + GetPlayers<Werewolf>() + "!\n";

            return message;
        }

        public override string PerformRole()
        {
            return NoCommand;
        }

        public override string TakeInput(String command)
        {
            return NoAction;
        }
    }

    public class Seer : WerewolfRole
    {
        public ulong observedID;

        public int []pileID;

        public Seer(WerewolfGame game, User owner)
        {
            this.game = game;
            this.owner = owner;
            this.order = 5;

            pileID = new int[2];
        }

        public override String MessageRole()
        {
            string message = "";

            message += "The seer can peer into what is, and what could be.\n";
            message += "You may either see another individual's role, or you may see two roles from the center pile\n";
            message += "The command \"/Seer User\" will reveal to you the role of the \"User\".";
            message += "You may also run the command \"/Seer Number1 Number 2\", which will allow you to roles at pile locations Number1 and Number 2.\n";
            message += "The players you may choose from are: " + GetPlayers<WerewolfRole>() + "\n";
            message += ("Valid entries for \"Number1\" and \"Number2\" are 1 - " + game.CenterPile.Length);
            message += ". Make sure they aren't the same ones!";
            message += "Remember, you will only be able to run one of these commands during the night, so choose wisely!";

            return message;
        }

        public override string PerformRole()
        {
            return "The roles at " + pileID[0] + " and " + pileID[1] +
                    " are " + game.CenterPile[pileID[0]] + " and " + game.CenterPile[pileID[1]]
                    + ", respectively.";
        }

        public override string TakeInput(String command)
        {
            String []tokens = command.Split(' ');

            if(acted == true)
            {
                return AlreadyActed;
            }

            if (tokens.Length == 3)
            {
                int []pile = new int[2];

                try
                {
                    pile[0] = int.Parse(tokens[1]) - 1;
                    pile[1] = int.Parse(tokens[2]) - 1;
                }
                catch 
                {
                    return "There was an error while parsing the numbers";
                }

                if (pile[0] == pile[1])
                {
                    return "You should try selecting two different numbers";
                }
                pileID = pile;

                acted = true;

                return CommandSuccess;
            }
            else if (tokens.Length == 2)
            {
                foreach (User item in game.channel.Server.Users)
                {
                    if (item.Name == tokens[1])
                    {
                        observedID = item.Id;

                        acted = true;

                        return CommandSuccess;
                    }
                }

                return UsernotFound(tokens[1]);
            }

            //tokens aren't valid
            return InvlaidWordCount;
        }
    }
    public class Robber : WerewolfRole
    {
        ulong playerSwitchID;        

        public Robber(WerewolfGame game, User owner)
        {
            this.game = game;
            this.owner = owner;
            this.order = 6;
        }

        public override String MessageRole()
        {
            string message = "";

            message += "The robber may or may not be unsatisfied with his position, so can choose to steal someone elses role!\n";
            message += "You may choose to switch your role with that of another players's role. If you do, you then see your new role.\n";
            message += "The robber is on the villagers team, but if you choose to switch your role with someone, you are on the team designated by your new card";
            message += "The command \"/Robber User\" will switch your role with the role you put for \"User\"\n";
            message += "Valid players are: " + GetPlayers<WerewolfRole>() + "\n";

            return message;
        }

        public override string PerformRole()
        {
            return game.SwapRoles(owner.Id, playerSwitchID);
        }

        public override string TakeInput(String command)
        {
            String[] tokens = command.Split(' ');

            if (acted == true)
            {
                return AlreadyActed;
            }

            if (tokens.Length != 2)
            {
                return InvlaidWordCount;
            }

            foreach (User user in game.channel.Server.Users)
            {
                if (user.Name == tokens[1])
                {
                    playerSwitchID = user.Id;

                    acted = true;

                    return CommandSuccess;
                }
            }

            return this.UsernotFound(tokens[1]);
        }
    }
    public class TroubleMaker : WerewolfRole
    {
        ulong []userID;

        public TroubleMaker(WerewolfGame game, User owner)
        {
            this.game = game;
            this.owner = owner;
            this.order = 7;
            
        }
        public override String MessageRole()
        {
            string message = "";

            message += "The troublemaker is always looking to ruin people's day, and can offen succeed!\n";
            message += "You switch two roles of players, but you do not look at what either of them are.\n";
            message += VillagersTeam;
            message += "The command \"/Troublemaker User1 User2\" will switch the roles of the two users\n";
            message += "Players you may choose from are: " + GetPlayers<WerewolfRole>() + "\n";

            return message;
        }

        public override string PerformRole()
        {
            return game.SwapRoles(userID[0], userID[1]);
        }

        public override string TakeInput(String command)
        {
            String[] tokens = command.Split(' ');

            if (acted == true)
            {
                return AlreadyActed;
            }

            if (tokens.Length != 3)
            {
                return InvlaidWordCount;
            }

            int found = 0;

            for (int j = 0; j < 2; j++)
            {
                foreach(User user in game.channel.Users)
                {
                    if(user.Name == tokens[j] + 1)
                    {
                        userID[j] = user.Id;
                        found++;
                        break;
                    }
                }
            }

            if (found != 2)
            {
                userID = new ulong[2];
                return UsernotFound(tokens[1]) + " or " + UsernotFound(tokens[2]) + " was not found .\n";
            }

            acted = true; 

            return CommandSuccess;
        }
    }
    public class Tanner : WerewolfRole
    {
        public Tanner(WerewolfGame game, User owner)
        {
            this.game = game;
            this.owner = owner;
            this.order = -1;
            acted = true;
        }
        public override String MessageRole()
        {
            string message = "";

            message += "The tanner hates his job, and actively wants to get killed by the towns people!\n";
            message += "Your goal is to get yourself killed. You win the game if and only if you get killed.\n";
            message += NoCommands;

            return message;
        }

        public override string PerformRole()
        {
            return NoAction;
        }

        public override string TakeInput(string command)
        {
            return NoCommand;
        }
    }
    public class Drunk : WerewolfRole
    {
        int pileselect;

        public Drunk(WerewolfGame game, User owner)
        {
            this.game = game;
            this.owner = owner;
            this.order = 8;
        }
        public override String MessageRole()
        {
            string message = "";

            message += "The Drunk is so drunk, you don't remember what role you are.\n";
            message += "At the end of the night, you switch your role with one at the center pile. You do not look at your new role\n";
            message += VillagersTeam;
            message += "The command \"/Drunk Number\" will replace your role with role in the corresponding center pile\n";
            message += ("Valid entries for \"Number\" are 1 - " + game.CenterPile.Length);

            return message;
        }

        public override string PerformRole()
        {
            return game.SwitchWithCenter(owner.Id, pileselect);
        }

        public override string TakeInput(String command)
        {
            String[] tokens = command.Split(' ');

            if (acted == true)
            {
                return AlreadyActed;
            }

            if (tokens.Length != 2)
            {
                return InvlaidWordCount;
            }

            try
            {
                pileselect = int.Parse(tokens[1]) - 1;
            }
            catch
            {
                return tokens[1] + " is not a valid number";
            }

            if (pileselect < 0 || pileselect >= game.CenterPile.Length)
            {
                return pileselect + " was not a valid slot for the center pile";
            }

            acted = true;

            return CommandSuccess;
           

        }
    }
    public class Hunter : WerewolfRole
    {
        public Hunter(WerewolfGame game, User owner)
        {
            this.game = game;
            this.owner = owner;
            this.order = -1;
        }
        public override String MessageRole()
        {
            String message = "";

            message += "The Hunter a skilled shot, and you get to kill someone!\n";
            message += "When you vote at the end of the game to execute an individual, the person you vote for always dies too, even if they don't have the plurality.\n";
            message += VillagersTeam;
            message += NoCommands;

            return message;
        }

        public override string PerformRole()
        {
            return NoAction;
        }

        public override string TakeInput(string command)
        {
            return NoCommands;
        }
    }
    public class Mason : WerewolfRole
    {
        public Mason(WerewolfGame game, User owner)
        {
            this.game = game;
            this.owner = owner;
            this.order = 4;
        }
        public override String MessageRole()
        {
            string message = "";

            message += "The Masons are a like twins, and each Mason knows who the other Mason is.\n";
            message += "You are a Mason, there is one other Mason in this game. If no one else has the role, it is in the center pile";
            message += VillagersTeam;
            message += NoCommands;
            message += "The other mason is " + GetPlayers<Mason>() + "\n";

            return message;
        }

        public override string PerformRole()
        {
            return NoAction;
        }

        public override string TakeInput(string command)
        {
            return NoCommands;
        }
    }
    public class Insomniac : WerewolfRole
    {
        public Insomniac(WerewolfGame game, User owner)
        {
            this.game = game;
            this.owner = owner;
            this.order = 9;
        }
        public override String MessageRole()
        {
            String message = "";

            message += "The Insomniac constantly wakes up, and you get to check what role you are at the end of the night.\n";
            message += "At the end of the night, you get to see your role. It very well might have changed duringt he night.\n";
            message += VillagersTeam;
            message += NoCommands;

            return message;
        }

        public override string PerformRole()
        {
            owner.SendMessage("Over the night you have changed! You are now a " + game.Players[owner.Id] + "!\n");
            return "A message to the player has been sent!";
        }

        public override string TakeInput(string command)
        {
            return NoCommands;
        }
    }
    public class Minion : WerewolfRole
    {
        public Minion(WerewolfGame game, User owner)
        {
            this.game = game;
            this.owner = owner;
            this.order = 3;
        }
        public override String MessageRole()
        {
            string message = "";

            message += "The Minion is pure evil, and want the werewolves to succeed!\n";
            message += "You get to know who the werewolves are, but they do not know who you are, your goal is to make sure they win!\n";
            message += WerewolvesTeam;
            message += NoCommands;
            message += "The Werewolves are: " + GetPlayers<Werewolf>() + "!\n";

            return message;
        }

        public override string PerformRole()
        {
            return NoAction;
        }

        public override string TakeInput(string command)
        {
            return NoCommands;
        }
    }
    public class Doppleganger : WerewolfRole
    {
        public Doppleganger(WerewolfGame game, User owner)
        {
            this.game = game;
            this.owner = owner;
        }
        public override String MessageRole()
        {
            return "";
        }

        public override string PerformRole()
        {
            throw new NotImplementedException();
        }

        public override string TakeInput(string command)
        {
            throw new NotImplementedException();
        }
    }



    public class WerewolfGame
    {
        //Keep track of the channel the game was initiated on
        public Channel channel;

        //Role to be set for default player
        public Role playerRole;

        //Role for nonplayers of the game
        public Role nonRole;

        //dictionary of all the players and their role
        public Dictionary<ulong, WerewolfRole> Players;

        //list of all the admins
        public List<ulong> Admins;

        //list of all the valid role types
        public HashSet<WerewolfRole> ValidRoles;

        public HashSet<ulong> hasPerformed;

        //All Roles in the game
        public List<WerewolfRole> PlayingRoles;

        public WerewolfRole[] CenterPile;

        public Dictionary<ulong, ulong> VotedFor;

        public List<ulong> lynched;
        /// <summary>
        /// Number of roles in addition to the number of players
        /// </summary>
        public int additionalRoles;

        //keeps track of the current game state
        public enum gamePhase
        {
            Signup,
            Perform,
            Vote,
            End
        }
        gamePhase currentGameState;

        /// <summary>
        /// Used to determine the number of werewolves in the pool := (AddRoles + PlayerCount) / WWMod
        /// </summary>
        public int werewolfModifier;

        /// <summary>
        /// Used to determine number of villagers in the pool := (AddRoles + PlayerCount) / VilMod
        /// </summary>
        public int villagerModifier;

        //used to see if a game should debug all the events.
        public bool debug;

        /// <summary>
        /// Constructs the parameters for initializing the bot
        /// </summary>
        /// <param name="HardMode"> True if include Doppleganger, left out if false</param>
        public WerewolfGame(Channel channel, Role nonRole, Role playerRole, bool debug = false, int additionalRoles = 3, int werewolfModifier = 3, int villagerModifier = 3)
        {
            //initiate
            Players = new Dictionary<ulong, WerewolfRole>();
            Admins = new List<ulong>();
            ValidRoles = new HashSet<WerewolfRole>();
            PlayingRoles = new List<WerewolfRole>();
            CenterPile = new WerewolfRole[additionalRoles];
            VotedFor = new Dictionary<ulong, ulong>();
            lynched = new List<ulong>();


            this.channel = channel;
            this.debug = debug;
            this.additionalRoles = additionalRoles;
            this.werewolfModifier = werewolfModifier;
            this.villagerModifier = villagerModifier;
            this.nonRole = nonRole;
            this.playerRole = playerRole;

            currentGameState = gamePhase.Signup;

            //now we get the valid roles
            ValidRoles.Add(new Werewolf(this, null));
            ValidRoles.Add(new Minion(this, null));
            ValidRoles.Add(new Mason(this, null));
            ValidRoles.Add(new Seer(this, null));
            ValidRoles.Add(new Robber(this, null));
            ValidRoles.Add(new TroubleMaker(this, null));
            ValidRoles.Add(new Drunk(this, null));
            ValidRoles.Add(new Insomniac(this, null));
        }

        public String AddPlayer(ulong playerID)
        {
            if (Players.ContainsKey(playerID))
            {
                return (channel.GetUser(playerID).Name + " is already in the game");
            }

            if (currentGameState != gamePhase.Signup)
            {
                return (channel.GetUser(playerID).Name + " cannot be added as the game as roles have already been assigned");
            }
            
            Players.Add(playerID, null);

            return (channel.GetUser(playerID).Name + " has joined the game");
        }

        public String RemovePlayer(ulong playerID)
        {
            if (!Players.ContainsKey(playerID))
            {
                return (channel.GetUser(playerID).Name + " is not in the game, and therefore cannot be removed");
            }

            if (currentGameState != gamePhase.Signup)
            {
                return (channel.GetUser(playerID).Name + " cannot be removed as the game as roles have already been assigned");
            }
            Players.Remove(playerID);
            return (channel.GetUser(playerID).Name + " has left the game");
        }

        /// <summary>
        /// Adds the player id to the admin list, will be able to perform commands on the game.
        /// Does not need to be a player in the game for this
        /// </summary>
        /// <param name="playerID"> Player to be added</param>
        /// <returns></returns>
        public String AddAdmin(ulong playerID)
        {
            if (Admins.Contains(playerID))
            {
                return channel.Server.GetUser(playerID) + " is already an admin to the game!";
            }

            Admins.Add(playerID);

            return channel.Server.GetUser(playerID) + " is now an admin!";
        }


        /// <summary>
        /// Removes the player id from the list of admins, will no longer be able to moderate games
        /// </summary>
        /// <param name="playerID">Player to be removed from admin slot</param>
        /// <returns></returns>
        public String RemoveAdmin(ulong playerID)
        {
            if (!Admins.Contains(playerID))
            {
                return channel.Server.GetUser(playerID) + " is not an admin, therefore cannot be removed!";
            }

            Admins.Remove(playerID);

            return channel.Server.GetUser(playerID) + " is no longer an admin.";
        }

        public String AddRole(String role)
        {

            if (currentGameState != gamePhase.Signup)
            {
                return ("Roles cannot be added as the game as roles have already been assigned");
            }

            switch (role)
            {
                case "Observer":
                    ValidRoles.Add(new Observer(this, null));
                    break;
                case "Werewolf":
                    ValidRoles.Add(new Werewolf(this, null));
                    break;
                case "Seer":
                    ValidRoles.Add(new Seer(this, null));
                    break;
                case "Robber":
                    ValidRoles.Add(new Robber(this, null));
                    break;
                case "Troublemaker":
                    ValidRoles.Add(new TroubleMaker(this, null));
                    break;
                case "Tanner":
                    ValidRoles.Add(new Tanner(this, null));
                    break;
                case "Drunk":
                    ValidRoles.Add(new Drunk(this, null));
                    break;
                case "Hunter":
                    ValidRoles.Add(new Hunter(this, null));
                    break;
                case "Mason":
                    ValidRoles.Add(new Mason(this, null));
                    break;
                case "Insomniac":
                    ValidRoles.Add(new Insomniac(this, null));
                    break;
                case "Minion":
                    ValidRoles.Add(new Minion(this, null));
                    break;
                case "Doppleganger":
                    ValidRoles.Add(new Doppleganger(this, null));
                    break;
                default: 
                    return role + " was not a valid role to add";                     
            }
                
            return (role + " has been added to the game");
        }

        public String RemoveRole(String role)
        {
            if (currentGameState != gamePhase.Signup)
            {
                return ("Roles cannot be removed as the game as roles have already been assigned");
            }

            switch (role)
            {
                case "Observer":
                    ValidRoles.Remove(new Observer(this, null));
                    break;
                case "Werewolf":
                    ValidRoles.Remove(new Werewolf(this, null));
                    break;
                case "Seer":
                    ValidRoles.Remove(new Seer(this, null));
                    break;
                case "Robber":
                    ValidRoles.Remove(new Robber(this, null));
                    break;
                case "Troublemaker":
                    ValidRoles.Remove(new TroubleMaker(this, null));
                    break;
                case "Tanner":
                    ValidRoles.Remove(new Tanner(this, null));
                    break;
                case "Drunk":
                    ValidRoles.Remove(new Drunk(this, null));
                    break;
                case "Hunter":
                    ValidRoles.Remove(new Hunter(this, null));
                    break;
                case "Mason":
                    ValidRoles.Remove(new Mason(this, null));
                    break;
                case "Insomniac":
                    ValidRoles.Remove(new Insomniac(this, null));
                    break;
                case "Minion":
                    ValidRoles.Remove(new Minion(this, null));
                    break;
                case "Doppleganger":
                    ValidRoles.Remove(new Doppleganger(this, null));
                    break;
                default:
                    return role + " was not a valid role to add";
            }

            return (role + " has been removed from the game");
        }

        public WerewolfRole GetRole(ulong playerID)
        {
            return Players[playerID];
        }

        public String SetExtraRoles(int roleCount)
        {
            if (currentGameState != gamePhase.Signup)
            {
                return ("The number of extra roles cannot be changed as roles have already been assigned");
            }
            additionalRoles = roleCount;
            CenterPile = new WerewolfRole[additionalRoles];
            return ("There will be " + additionalRoles + " more roles in the game than players");
        }

        public String SetWerewolfModifier(int WWMod)
        {
            if (currentGameState != gamePhase.Signup)
            {
                return ("The number of werewolves cannot be modified while the game is running");
            }
            werewolfModifier = WWMod;
            return ("There will be a werewolf per " + werewolfModifier + " players");
        }

        public String SetVillagerModifier(int VMod)
        {
            if (currentGameState != gamePhase.Signup)
            {
                return ("The number of villagers cannot be modified while the game is running");
            }
            villagerModifier = VMod;
            return ("There will be a villagers per " + villagerModifier + " players");
        }

        public String SwapRoles(ulong playerID1, ulong playerID2)
        {
            String debug = (playerID1 + " was " + Players[playerID1]);
            debug += ( " and " + playerID2 + " was " + Players[playerID2]);

            WerewolfRole temp = Players[playerID1];
            Players[playerID1] = Players[playerID2];
            Players[playerID2] = temp;

            debug += (" but " + playerID1 + " is now " + Players[playerID1]);
            debug += (" and " + playerID2 + " is now " + Players[playerID2]);

            return debug;
        }

        public String SwitchWithCenter(ulong playerID, int pileSlot)
        {
            WerewolfRole temp = Players[playerID];
            Players[playerID] = CenterPile[pileSlot];
            CenterPile[pileSlot] = temp;

            return (channel.GetUser(playerID).Name + " was " + CenterPile[pileSlot] + " But is now " + Players[playerID]);

        }


        /// <summary>
        /// Resets the game, only keeps the discord roles, and the players in the game
        /// </summary>
        /// <returns></returns>
        public String ResetGame()
        {
            return "The game has been reset! " + GetGameState();
        }

        /// <summary>
        /// Generates the roles for the game randomly using the rules and the number of players
        /// </summary>
        public String GenerateRoles()
        {
            if (currentGameState != gamePhase.Signup)
            {
                return ("Roles cannot be generated at this time, as the roles have already been assigned");
            }

            //we want to make it so that names can no longer be changed
            

            int size = Players.Count + additionalRoles;

            int werewolves = size / werewolfModifier;

            int villagers = size / villagerModifier;

            //adding the correct snumber of werewolves
            for(int j = 0; j < werewolves; j++)
            {
                PlayingRoles.Add(new Werewolf(this,null));
                //decriment size, as a role is added
                size--;
            }

            //werewolves can no longer be chosen at this point;
            RemoveFromValid(new Werewolf(this, null));

            //get our random number
            Random rand = new Random();

            for (int j = size; j > 0; j--)
            {
                //removes masons if only a single iteration left, assumes always two masons
                if (j == 1)
                {
                    RemoveFromValid(new Mason(this, null));
                }

                //removes villager if no villagers left 
                if (villagers == 0)
                {
                    RemoveFromValid(new Villager(this, null));
                }

                //now lets get the actual number
                WerewolfRole rawRole = ValidRoles.ElementAt<WerewolfRole>(rand.Next(ValidRoles.Count));

                //check for masons, if so, add an additional mason and add the counter up one
                if(rawRole.GetType() == typeof(Mason))
                {                        
                    PlayingRoles.Add(new Mason(this,null));
                    j--;
                }
                //now we want to add that role, masons are added twice, so no issue there
                PlayingRoles.Add(rawRole);

                if(rawRole.GetType() != typeof(Villager))
                {
                    RemoveFromValid(rawRole);
                }
                else
                {
                    villagers -= 1;
                }
                    
            }

            return GetGameState();
        }

        public String AssignRoles()
        {
            if (currentGameState != gamePhase.Signup)
            {
                return ("Roles cannot be assigned as they have already been assigned");
            }

            //We want to make sure we properly shuffly the list
            Shuffle(PlayingRoles);

            //now we give a role to each player
            int count = 0;
            foreach(KeyValuePair<ulong, WerewolfRole> item in new Dictionary<ulong, WerewolfRole>(Players))
            {
                //now we give them the role according to j
                Players[item.Key] = PlayingRoles[count];
                count++;
            }

            //each player should have their assigned roles at this point

            //now we put the rest of the roles into the center array
            for(int j = 1; j <= additionalRoles; j++)
            {             
                CenterPile[j - 1] = PlayingRoles[PlayingRoles.Count - j];
            }

            //assign state
            currentGameState = gamePhase.Perform;

            return GetGameState();

        }

        public string Perform()
        {
            int roleAmount = 10;
            WerewolfRole[] roles = new WerewolfRole[roleAmount];

            foreach(KeyValuePair<ulong, WerewolfRole> item in Players)
            {
                if (item.Value.order != -1 && item.Value.acted == true)
                {
                    if (roles[item.Value.order] != null)
                    {
                        return "Two roles have the same order";
                    }
                    roles[item.Value.order] = item.Value;
                }
            }

            //now we want to go into each and perform
            for (int j = 0; j < roleAmount; j++)
            {
                if (roles[j] != null)
                {
                    roles[j].PerformRole();
                }
            }

            currentGameState = gamePhase.Vote;

            return "All roles have been performed, the new state of the game is: \n" + GetGameState();
        }

        public string Vote(User voter, string command)
        {
            String[] tokens = command.Split(' ');

            if (tokens.Length != 2)
            {
                return "The command is invalid for voting";
            }
            if (currentGameState != gamePhase.Vote)
            {
                return ("Voting is not allowed at this time");
            }
            if (VotedFor.ContainsKey(voter.Id))
            {
                return "You have already voted!";
            }

            User victim = null;

            foreach (User user in channel.Users)
            {
                if (user.Name == tokens[1])
                {
                    victim = user;
                }
            }

            if (victim == null)
            {
                return tokens[1] + " was not found within this game, please try again!";
            }

            VotedFor[voter.Id] = victim.Id;

            return voter.Name + " has voted to kill " + victim.Name + "!\n";

        }

        public string ExecutePlayer()
        {
            if (currentGameState != gamePhase.Vote)
            {
                return "Players must be allowed to vote before someone is lynched!";
            }

            Dictionary<ulong, int> tallies = new Dictionary<ulong, int>();

            foreach (ulong player in Players.Keys)
            {
                tallies.Add(player, 0);
            }

            foreach (ulong victim in VotedFor.Values)
            {
                tallies[victim] += 1;
            }

            //now we want to check to see who have the most votes
            List<ulong> execute = new List<ulong>();
            int againstfor = 0;

            foreach (KeyValuePair<ulong, int> victim in tallies)
            {
                if (victim.Value == againstfor)
                {
                    execute.Add(victim.Key);
                }

                if (victim.Value > againstfor)
                {
                    execute = new List<ulong>();
                    execute.Add(victim.Key);
                }

                //otherwise we just do nothing

            }

            String message = "The results are in, the players who received the most votes are: ";

            foreach (ulong item in execute)
            {
                message += channel.GetUser(item).Name + " who was a " + Players[item] + "!";
                lynched.Add(item);
            }

            message += "!\n";

            foreach (KeyValuePair<ulong, WerewolfRole> item in Players)
            {
                if (item.Value.GetType() == typeof(Hunter))
                {
                    message += "Oh, and " + channel.GetUser(item.Key).Name + " was the Hunter, so " + channel.GetUser(VotedFor[item.Key]).Name +
                        "was also killed.\n";
                    lynched.Add(VotedFor[item.Key]);
                }
            }

            currentGameState = gamePhase.End;

            //now we want to declare who the winners are

            foreach (ulong item in lynched)
            {
                if (Players[item].GetType() == typeof(Tanner))
                {
                    message += "The Tanner was killed, therefore " + Players[item] + "is the only winner in the round!";
                    return message;
                }
            }

            foreach (ulong item in lynched)
            {
                if (Players[item].GetType() == typeof(Werewolf))
                {
                    message += "A werewolf was killed, therefore the winners are!: ";

                    foreach (KeyValuePair<ulong, WerewolfRole> player in Players)
                    {
                        if (player.Value.GetType() != typeof(Werewolf) && 
                            player.Value.GetType() != typeof(Minion) &&
                            player.Value.GetType() != typeof(Tanner))
                        {
                            message += channel.GetUser(player.Key).Name + " ";
                        }
                    }

                    return message;
                }
            }

            //a tanner nor werewolf has been killed
            message += "A werewolf has not been killed, therefore the winners are!: ";

            foreach (KeyValuePair<ulong, WerewolfRole> player in Players)
            {
                if (player.Value.GetType() == typeof(Werewolf) ||
                    player.Value.GetType() == typeof(Minion))
                {
                    message += channel.GetUser(player.Key).Name + " ";
                }
            }
            return message;
        }

        private void Shuffle<T>(List<T> deck)
        {
            Random rand = new Random();
            for (int n = deck.Count - 1; n > 0; --n)
            {
                int k = rand.Next(n + 1);   
                T temp = deck[n];
                deck[n] = deck[k];
                deck[k] = temp;
            }
        }

        public string GetGameState()
        {
            String debug = "Right now,  ";
               

            //now we debug
            foreach (KeyValuePair<ulong, WerewolfRole> item in Players)
            {
                if (item.Value == null)
                {
                    debug += (channel.GetUser(item.Key) + " has no role, ");
                }
                else
                {
                    debug += (channel.GetUser(item.Key) + " is " + item.Value.GetType().Name + ", ");
                }
            }

            debug += "\nThe center pile is set for" + additionalRoles + " roles: ";

            foreach (WerewolfRole item in CenterPile)
            {
                if (item == null)
                {
                    debug += ("None ");
                }
                else
                {
                    debug += (item.GetType().Name + " ");
                }

            }

            return debug;
        }

        private void RemoveFromValid(WerewolfRole role)
        {
            WerewolfRole temp = null;

            foreach(WerewolfRole item in ValidRoles)
            {
                if (role.GetType() == item.GetType())
                {
                    temp = item;
                }
            }

            ValidRoles.Remove(temp);
        }



    }
}