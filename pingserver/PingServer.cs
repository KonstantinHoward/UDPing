using System.Text;
using System.Net.Sockets;
using System.Net;
using System.ComponentModel;

namespace Ping
{
    // struct to store state of UDP listener throughout reply handling
    public struct State {
        // UDP socket listening for replies to server's pings
        public UdpClient u;
        // incoming client IP endpoints it is listening to - any address from PORT
        public IPEndPoint e;
    };

    class PingServer {
        /*  Constants */
        private const int PORT = 65432;             // port on server and client machines for UDP listening
        private const int MAX_TIME = 4000000;       // maximum allowed roundtrip time in microseconds for one ping (4 seconds)
        private const int CHECK = 255;              // check value sent in all replies  
        private const short TTL = 128;              // time to live of packets sent out by server, default is 128
        private const short PACKAGE_SIZE = 32;      // size of package sent in ping
        /* Static Members (initialized in constructor) */
        public static TimeOnly[,] sendTimes;        // timestamps of sent pings
        public static int[,] elapsedTimes;          // roundtrip ping times in microseconds
        public static bool receiving;               // block while receiving ping replies
        /* Class Members */
        private readonly UdpClient sender;          // UdpClient for sending pings
        private readonly UdpClient listener;        // UdpClient to listen for replies
        private readonly IPAddress[] iPs;           // IP addresses to be pinged
        private readonly IPEndPoint[] destPoints;   // IPs as end points

        
        
        // constructor, also initializes static members
        public PingServer(List<IPAddress> ipList, int numPings) {
            this.iPs = ipList.ToArray();
            this.destPoints = new IPEndPoint[iPs.Length];
            for (int i = 0; i < destPoints.Length; i++) {
                destPoints[i] = new IPEndPoint(iPs[i], PORT+1);
            }
            
            sendTimes = new TimeOnly[this.iPs.Length, numPings];
            elapsedTimes = new int[this.iPs.Length, numPings];
            this.sender = new UdpClient(0) {
                Ttl = TTL
            };
            this.listener = new UdpClient(PORT);
        }

        // handles manual IP address entry, reading from command line
        // @return List of entered IPAddress objects to initizalize PingServer
        public static List<IPAddress> ManualInput() {
            Console.Write("Enter IPs (space-separated): ");
            string input = Console.ReadLine();

            List<string> ipStrings = input.Split(' ').ToList();     
            List<IPAddress> addrs = new(); 

            ipStrings.ForEach(ip => addrs.Add(IPAddress.Parse(ip)));
            return addrs;
        }

        // handles IP address entry from text file, currently limited to 256 addresses
        // @return List of entered IPAddress objects to initizalize PingServer
        public static List<IPAddress> FileInput() {
            Console.Write("Enter path to text file: ");
            // path is relative to directory containing program files
            string path = Console.ReadLine();
            while (!File.Exists(path)) {
                Console.Write("File does not exist. Please enter the full path: ");
                path = Console.ReadLine();
            }

            using StreamReader reader = File.OpenText(path);
            List<IPAddress> addrs = new();
            string line;
            
            while ((line = reader.ReadLine()) != null) {
                try {
                    addrs.Add(IPAddress.Parse(line));
                }
                catch(Exception e) {
                    Console.WriteLine("Invalid IP in file.   " + line);
                    Console.WriteLine(e);
                    return null;
                }
            }
            return addrs;
        }

        // handles IP address entry as subnet notation
        // @return List of entered IPAddress objects to initizalize PingServer
        public static List<IPAddress> SubnetInput() {
            Console.Write("Enter the subnet CIDR notation (/24-32 accepted): ");
            string input = Console.ReadLine();
            int mask = Int32.Parse(input.Substring(input.Length-2));
            string addr = input.Substring(0,input.Length-3);
            IPAddress a; // if successful, original IP stored here

            // check input for CIDR formatting, mask range, and address correctness
            while (!(input[^3] == '/' && mask <= 32 && mask >= 24 && IPAddress.TryParse(addr,out a))) {
                Console.Write("Invalid CIDR. Enter in the form x.x.x.x/m where 24<=m<=32. ");
                input = Console.ReadLine();
                mask = Int32.Parse(input.Substring(input.Length-2));
                addr = input.Substring(0,input.Length-3);
            }
            // corner case: network is one address
            if (mask == 32) return new List<IPAddress>() {a};

            int finalByte = Int32.Parse(addr.Split(".")[^1]);
            int numBlocks = (int) Math.Pow(2.0, (mask - 24));
            int blockSize = 256 / numBlocks;
            int contBlock = numBlocks;

            for (int block = numBlocks; block > 0; block--) {
                if (finalByte < block * blockSize) contBlock = block;
                else break;
            }
            List<byte> addy = new();
            addr.Split(".").ToList().ForEach(p => addy.Add(Byte.Parse(p)));
            byte[] address = addy.ToArray();
            address[3] = (byte) ((contBlock-1) * blockSize);

            // offset for /31 where usable addresses == # addresses
            if (mask == 31) {
                blockSize+=2;
                address[3]--;
            }

            List<IPAddress> addrs = new();
            for (int j = 0; j < blockSize-2; j++) {
                address[3]++;
                addrs.Add(new IPAddress(address));
            }
            return addrs; 
        }

        // helper function begins listening for ping replies
        public void Listen() {
            State firstState = new() {
                u = this.listener,
                e = new IPEndPoint(IPAddress.Any, PORT+1)
            };
            this.listener.BeginReceive(new AsyncCallback(ProcessReply), firstState);
            receiving = true;
            //Console.WriteLine("Begin receiving");
        }
        
        // sends UDP pings to each IP destination asynchronously and marks departure time
        public void SendPings() {
            for (byte idx = 0; idx < destPoints.Length; idx++) {
                for (byte iter = 0; iter < elapsedTimes.GetLength(1); iter++) {
                    byte[] package = new byte[PACKAGE_SIZE];
                    package[0] = idx;
                    package[1] = iter;
                    sendTimes[idx,iter] = TimeOnly.FromDateTime(DateTime.Now);
                
                    sender.SendAsync(package, package.Length , destPoints[idx]);
                }
            }
        }

        // writes results to file in temp folder
        public string GiveResults() {
            // old path = c:\Temp\PingReport + DateTime.Now.ToString("MM-dd-HH:mm")
            string path = @"PingReport.txt";
            if (File.Exists(path)) {
                Console.Write("A previous PingReport exists. Rename the file or it will be deleted. Proceed? (y) ");
                string yes = Console.ReadLine();
                while (!yes.Equals("y")) {
                    Console.Write("Enter \'y\' when ready to proceed: ");
                    yes = Console.ReadLine();
                }  
            }
            using StreamWriter writer = new StreamWriter(path);
            writer.WriteLine($"Ping Report {DateTime.Now.ToString("MM-dd-HH:mm")}");
            writer.WriteLine($"Package size={PACKAGE_SIZE} bytes. TTL={TTL}.");
            
            int lost = 0;
            int timedOut = 0;
            for (byte id = 0; id < this.iPs.Length; id++) {
                    writer.WriteLine($"-----{elapsedTimes.GetLength(1)} pings to {this.iPs[id]}-----");
                    for (byte iter = 0; iter < elapsedTimes.GetLength(1); iter++) {
                        if (elapsedTimes[id,iter] == 0) {
                            writer.WriteLine($"{iter+1}. No reply.");
                            lost++;
                        }
                        else if (elapsedTimes[id,iter] > MAX_TIME) {
                            writer.WriteLine($"{iter+1}. Timeout.");
                            timedOut++;
                        }
                        else writer.WriteLine($"{iter+1}. Roundtrip in {elapsedTimes[id,iter]}us.");
                    }
                }
            Console.WriteLine($"{this.iPs.Length * elapsedTimes.GetLength(1)} sent. {lost} dropped. {timedOut} timed out.");
            writer.WriteLine("------------------------------");

            writer.Close();
            return path;
        }

        // process UDP replies to pings asynchronously upon returns to EndReceive
        // log roundtrip for ping and reply, then continue listening
        public static void ProcessReply(IAsyncResult result) {
            State listenerState = (State) result.AsyncState;
            byte[] buffer = listenerState.u.EndReceive(result, ref listenerState.e);
            TimeOnly returnTime = TimeOnly.FromDateTime(DateTime.Now); 
            
            // bad packet not a ping reply
            if (buffer[2] != CHECK) {
                listenerState.u.BeginReceive(new AsyncCallback(ProcessReply), listenerState);
                return;
            }
            
            byte id = buffer[0];
            byte iter = buffer[1];
            elapsedTimes[id, iter] = (returnTime - sendTimes[id, iter]).Microseconds;

            listenerState.u.BeginReceive(new AsyncCallback(ProcessReply), listenerState);

            // check for end conditions: last ping reply received or last ping has timed out
            if ( id == elapsedTimes.GetLength(0)-1 && iter == elapsedTimes.GetLength(1)-1 || 
            TimeOnly.FromDateTime(DateTime.Now) - sendTimes[sendTimes.GetLength(0)-1, sendTimes.GetLength(1)-1] > 
            new TimeSpan(MAX_TIME * TimeSpan.TicksPerMicrosecond)) {
                receiving = false;
                //Console.WriteLine($"Finish receiving last id, iter is {id},{iter}");
            } 
        }

        public static void Main() {
            // IP Address selection and resolution to List of addresses
            Console.WriteLine("Select IP Address entry method:");
            Console.WriteLine("1. Manual");
            Console.WriteLine("2. Text File");
            Console.WriteLine("3. Subnet");
            string selection = Console.ReadLine();
            while (!selection.Equals("1") && !selection.Equals("2") && !selection.Equals("3")) {
                Console.WriteLine("Selection must be 1-3.");
                selection = Console.ReadLine();
            }
            PingServer server;
            Console.Write("Enter number of pings per address: ");
            int num;
            while (!Int32.TryParse(Console.ReadLine(), out num))
            {
                Console.WriteLine("Invalid number. Try again.");
            }

            if (selection.Equals("1")) {
                server = new PingServer(ManualInput(), num);
            }
            else if (selection.Equals("2")) {
                server = new PingServer(FileInput(), num);
            }
            else  {
                server = new PingServer(SubnetInput(), num);
            }
           
            Console.WriteLine($"Pinging {server.iPs.Length} IP {(server.iPs.Length == 1 ? "address" : "addresses")}...");
            server.Listen();
            server.SendPings();

            // block Main execution while receiving
            while (receiving) {  
                if ( 
            TimeOnly.FromDateTime(DateTime.Now) - sendTimes[sendTimes.GetLength(0)-1, sendTimes.GetLength(1)-1] > 
            new TimeSpan(MAX_TIME * TimeSpan.TicksPerMicrosecond)) {
                receiving = false;
            } 
            }
            
            // write to output if manually entered, else write to file
            if (selection.Equals("1")) {
                Console.WriteLine($"Package size={PACKAGE_SIZE} bytes. TTL={TTL}.");
                int lost = 0;
                int timedOut = 0;
                for (byte id = 0; id < server.iPs.Length; id++) {
                    Console.WriteLine($"-----{elapsedTimes.GetLength(1)} pings to {server.iPs[id]}-----");
                    for (byte iter = 0; iter < elapsedTimes.GetLength(1); iter++) {
                        if (elapsedTimes[id,iter] == 0) {
                            Console.WriteLine($"{iter+1}. No reply."); 
                            lost++;
                        }
                        else if (elapsedTimes[id,iter] > MAX_TIME) {
                            Console.WriteLine($"{iter+1}. Timeout."); 
                            timedOut++;
                        }
                        else Console.WriteLine($"{iter+1}. Roundtrip in {elapsedTimes[id,iter]}us.");
                    }
                }
                Console.WriteLine($"{num*server.iPs.Length} sent. {lost} dropped. {timedOut} timed out.");
                Console.WriteLine("------------------------------");
            }
            else {
                Console.WriteLine("Report written to file at " + server.GiveResults());
            }
            // wait for key entry to close out
            Console.ReadKey();
            server.listener.Close();
            server.sender.Close();
        }
    }
}


