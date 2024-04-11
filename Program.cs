using Microsoft.VisualBasic;
using Newtonsoft.Json;
using System.IO.Pipes;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TecieTelegram
{
    internal class Program
    {
        private static int numThreads = 4;
        static long ChannelId = long.Parse(Environment.GetEnvironmentVariable("TeleChanID")??"null"); 
        static TelegramBotClient botClient = null;

        public static Task Main(string[] args) => MainAsync();
        public static async Task MainAsync()
        {
            //start up the bot
            botClient = new(Environment.GetEnvironmentVariable("TeleBotToken")??"null");
            botClient.StartReceiving(UpdateHandler, ErrorHandler);

            //start up the named pipe for updates
            int i;
            Thread[]? servers = new Thread[numThreads];

            Console.WriteLine("\n*** Tecie Telegram ***\n");
            Console.WriteLine("Server started, waiting for client connect...\n");
            for (i = 0; i < numThreads; i++)
            {
                servers[i] = new(ServerThread);
                servers[i]?.Start();
            }
            Thread.Sleep(250);
            while (i > 0)
            {
                for (int j = 0; j < numThreads; j++)
                {
                    if (servers[j] != null)
                    {
                        if (servers[j]!.Join(50))
                        {
                            Console.WriteLine($"Server thread[{servers[j]!.ManagedThreadId}] finished.");
                            servers[j] = new Thread(ServerThread);
                            servers[j]?.Start();
                        }
                    }
                }
            }
            Console.WriteLine("\nServer threads exhausted, exiting.");
        }

        public static async Task UpdateHandler(ITelegramBotClient bot, Update update, CancellationToken token)
        {
            if (update.Type != UpdateType.Message)
                return;
            if (update.Message!.Type != MessageType.Text)
                return;

            // recieve message from user, and respond accordingly!
            string recieved = update.Message.Text!;
            switch (recieved.ToLower())
            {
                case "/start":
                    await bot.SendTextMessageAsync(update.Message.Chat.Id, "Hello! What would you like a link to?\nTelegram Channel -> /TC\nTelegram Group -> /TG\nDiscord -> /Dis\nVRChat group -> /VRC");
                    break;
                case "/tc":
                    await bot.SendTextMessageAsync(update.Message.Chat.Id, "You can join the Telegram Channel here: https://t.me/thenergeticon for updates and annoucements!");
                    break;
                case "/tg":
                    await bot.SendTextMessageAsync(update.Message.Chat.Id, "You can join the Telegram Group here: https://t.me/thenergeticonchat to chat about the con!");
                    break;
                case "/ds":
                    await bot.SendTextMessageAsync(update.Message.Chat.Id, "You can join the Discord Server here: https://discord.gg/Rte9sbK76D for hanging out, getting announcements, joining events, and more!");
                    break;
                case "/vrc":
                    await bot.SendTextMessageAsync(update.Message.Chat.Id, "You can join the VRChat Group here: https://vrc.group/TEC.8265 to be allowed to join events!");
                    break;
                default:
                    await bot.SendTextMessageAsync(update.Message.Chat.Id, "Sorry, I don't know that!");
                    break;
            }
        }

        public static async Task ErrorHandler(ITelegramBotClient bot, Exception exception, CancellationToken token)
        {
            Console.WriteLine($"\nBOT ERROR\n{exception.Message}\n\n{exception.StackTrace}\n");
        }

        private static void ServerThread()
        {
            NamedPipeServerStream pipeServer =
                new NamedPipeServerStream("TecieTelegramPipe", PipeDirection.InOut, numThreads);

            int threadId = Thread.CurrentThread.ManagedThreadId;

            // Wait for a client to connect
            pipeServer.WaitForConnection();

            Console.WriteLine($"Client connected on thread[{threadId}].");
            try
            {
                // Read the request from the client. Once the client has
                // written to the pipe its security token will be available.

                StreamString ss = new StreamString(pipeServer);
                string authkey = Environment.GetEnvironmentVariable("TECKEY") ?? "no key found";

                // Verify our identity to the connected client using a
                // string that the client anticipates.
                if (ss.ReadString() != authkey) { ss.WriteString("Unauthorized client!"); throw new Exception("Unauthorized client connection attemted!"); }
                ss.WriteString(authkey);
                string operation = ss.ReadString(); // E for event ping  A for announcement  U for update

                string post = "";
                ss.WriteString("READY");
                string message = ss.ReadString();

                switch (operation)
                {
                    case "A":
                        post = message;
                        botClient.SendTextMessageAsync(ChannelId, post);
                        break;
                    case "E":
                        EventPingInfo eventinfo = JsonConvert.DeserializeObject<EventPingInfo>(message)!;
                        Console.WriteLine(JsonConvert.SerializeObject(eventinfo, Formatting.Indented));
                        post = $"An event is starting!\n\n{eventinfo.EventName}\n\n{eventinfo.EventDescription}\n\n{(eventinfo.EventLink != null ? $"Join Here! {eventinfo.EventLink}\nSee the current event here: https://thenergeticon.com/Events/currentevent" : "See the current event here: https://thenergeticon.com/Events/currentevent")}";
                        botClient.SendTextMessageAsync(ChannelId, post);
                        break;
                    case "U":
                        post = $"Update: {message}";
                        botClient.SendTextMessageAsync(ChannelId, post);
                        break;
                    default:
                        Console.WriteLine("Invalid operation");
                        return;
                }
            }
            // Catch any exception thrown just in case sumn happens
            catch (Exception e)
            {
                Console.WriteLine($"ERROR: {e.Message}");
            }
            pipeServer.Close();
        }
    }

    class EventPingInfo(string name, string desc, string? link)
    {
        public string EventName = name;
        public string EventDescription = desc;
        public string? EventLink = link;
    }

    public class StreamString
    {
        private Stream ioStream;
        private UnicodeEncoding streamEncoding;

        public StreamString(Stream ioStream)
        {
            this.ioStream = ioStream;
            streamEncoding = new UnicodeEncoding();
        }

        public string ReadString()
        {
            int len = 0;

            len = ioStream.ReadByte() * 256;
            len += ioStream.ReadByte();
            byte[] inBuffer = new byte[len];
            ioStream.Read(inBuffer, 0, len);

            return streamEncoding.GetString(inBuffer);
        }

        public int WriteString(string outString)
        {
            byte[] outBuffer = streamEncoding.GetBytes(outString);
            int len = outBuffer.Length;
            if (len > UInt16.MaxValue)
            {
                len = (int)UInt16.MaxValue;
            }
            ioStream.WriteByte((byte)(len / 256));
            ioStream.WriteByte((byte)(len & 255));
            ioStream.Write(outBuffer, 0, len);
            ioStream.Flush();

            return outBuffer.Length + 2;
        }
    }
}
