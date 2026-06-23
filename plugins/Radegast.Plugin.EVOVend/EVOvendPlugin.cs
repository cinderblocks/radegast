using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using LibreMetaverse;
using LibreMetaverse.StructuredData;
using System.Threading;
using System.IO;
using System.Net;

namespace Radegast.Plugin.EVOVend
{
    [Radegast.Plugin(Name = "EVOvend Plugin", Description = "EVO Vendor Delivery System", Version = "1.0")]
    public partial class EVOvendPlugin : RadegastTabControl, IRadegastPlugin
    {
        public int DELIVERY_INTERVAL
        {
            get
            {
                return (int)numDeliveryInterval.Value;
            }
            set
            {
                numDeliveryInterval.Value = value;
            }
        }

        public int STARTUP_DELAY
        {
            get
            {
                return (int)numStartupDelay.Value;
            }
            set
            {
                numStartupDelay.Value = value;
            }
        }

        public OSDMap config;

        public static System.Threading.Timer timer;
        private InventoryManager Manager;
        private LibreMetaverse.Inventory Inventory;

        private string vendURL = @"http://evosl.org/TREK/SL/SLvendor.php";
	private string loginURL = @"http://evosl.org/TREK/SL/vendor.php";
        List<InventoryBase> searchRes = new List<InventoryBase>();

        /*private GridClient Client { get { return instance.Client; } }*/

        static string tabID = "evovend_tab";
        static string tabLabel = "EVOvend";

        private string pluginName = "EVOvend";
        private string version = "1.0";
        private ToolStripMenuItem EVOButton;

        public EVOvendPlugin()
        {
            //this.InitializeComponent();
        }

        public EVOvendPlugin(RadegastInstance instance, bool unused) : base(instance)
        {
            //this.instance = instance;
            Init();
            Disposed += new EventHandler(EVOvendTab_Disposed);
            RegisterClientEvents(client);
        }

        void RegisterClientEvents(GridClient client)
        {
            //instance.ClientChanged += new EventHandler<ClientChangedEventArgs>(instance_ClientChanged);
            //client.Self.ChatFromSimulator += new EventHandler<ChatEventArgs>(Self_ChatFromSimulator);
        }

        void UnregisterClientEvents(GridClient client)
        {
            //if (client == null) return;
        }

        private UUID offeredObject;
        private UUID offeredAgent;

        void Inventory_InventoryObjectOffered(object sender, InventoryObjectOfferedEventArgs e)
        {
            offeredObject = e.ObjectID;
            offeredAgent = e.Offer.FromAgentID;
        }

        void Inventory_ItemReceived(object sender, ItemReceivedEventArgs e)
        {
            if (offeredObject != e.Item.UUID) return;
            if (offeredAgent == UUID.Zero)
            {
                return;
            }
            
            Dictionary<string, string> param = new Dictionary<string, string>();
            param.Add("invUUID", e.Item.UUID.ToString());
            param.Add("userUUID", offeredAgent.ToString());
            param.Add("name", e.Item.Name);

            if(e.Item.SaleType != SaleType.Not)
                param.Add("price", e.Item.SalePrice.ToString());
            else
                param.Add("price", "0");
            param.Add("texture", "");
            param.Add("botUUID", client.Self.AgentID.ToString());
            string str = this.RequestVendor("ADDPRODUCT", param);
            int result;

            Int32.TryParse(str, out result);

            if(result > 0){
                instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": Product " + e.Item.Name + " from Agent " + offeredAgent.ToString() + " accepted and inserted", ChatBufferTextStyle.StatusBlue);
                client.Self.InstantMessage(offeredAgent, "Your Object has been transfered successfully. Please visit " + this.loginURL + " for further steps");
            }
            else
            {
                // if there is ANY error, delete the object
                instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": ADDPRODUCT FAILED\n" + str + "\n\n[ITEM: " + e.Item.Name + " AGENT: " + offeredAgent.ToString() + "]", ChatBufferTextStyle.Error);
                client.Inventory.RemoveItem(e.Item.UUID);
            }

            offeredAgent = UUID.Zero;
        }
        

        void EVOvendTab_Disposed(object sender, EventArgs e)
        {
            this.writeConfig();
            UnregisterClientEvents(client);
            //instance.ClientChanged -= new EventHandler<ClientChangedEventArgs>(instance_ClientChanged);
        }

        public void StartPlugin(RadegastInstance inst)
        {
            instance = inst;
            instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + " version " + version + " loaded");

            Init();

            EVOButton = new ToolStripMenuItem(tabLabel, null, OnEVOButtonClicked);
            instance.MainForm.PluginsMenu.DropDownItems.Add(EVOButton);

            // setup timer
            timer = new System.Threading.Timer(new TimerCallback(productCallback));
            this.SetupTimer();
            // invenotry receiving
            client.Inventory.ItemReceived += Inventory_ItemReceived;
            client.Inventory.InventoryObjectOffered +=Inventory_InventoryObjectOffered;
        }

        public void SetupTimer(){
            if (timer == null) return;
            timer.Change((STARTUP_DELAY * 1000), (DELIVERY_INTERVAL * 1000));
            instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ":  Waiting " + STARTUP_DELAY + " seconds before start...");
        }

        private void Init()
        {
            this.InitializeComponent();
            this.RegisterClientEvents(client);
            if (instance != null)
            {
                this.readConfig();
            }
        }

        private void readConfig()
        {
            config = instance.GlobalSettings["plugin." + pluginName] as OSDMap;

            if (config == null)
            {
                config = new OSDMap();
                config["startup_delay"] = new OSDInteger(60);
                config["delivery_interval"] = new OSDInteger(60);
                instance.GlobalSettings["plugin." + pluginName] = config;
            }

            if (!config.ContainsKey("startup_delay"))
                config["startup_delay"] = 60;
            if (!config.ContainsKey("delivery_interval"))
                config["delivery_interval"] = 60;

            STARTUP_DELAY = config["startup_delay"].AsInteger();
            DELIVERY_INTERVAL = config["delivery_interval"].AsInteger();
        }

        private void writeConfig()
        {
            config = instance.GlobalSettings["plugin." + pluginName] as OSDMap;

            if (config != null)
            {
                config["startup_delay"] = STARTUP_DELAY;
                config["delivery_interval"] = DELIVERY_INTERVAL;
                //instance.GlobalSettings["plugin." + pluginName] = config;
            }

            instance.GlobalSettings.Save();
        }

        void OnEVOButtonClicked(object sender, EventArgs e)
        {
            if (instance.TabConsole.TabExists(tabID))
            {
                instance.TabConsole.Tabs[tabID].Select();
            }
            else
            {
                instance.TabConsole.AddTab(tabID, tabLabel, new EVOvendPlugin(instance, true));
                instance.TabConsole.Tabs[tabID].Select();
            }
        }

        public void StopPlugin(RadegastInstance instance)
        {
            // kill timer
            timer.Dispose();
            EVOButton.Dispose();
            // unregister inventory receiver events
            client.Inventory.ItemReceived -= Inventory_ItemReceived;
            client.Inventory.InventoryObjectOffered -= Inventory_InventoryObjectOffered;
        }

        private string m_searchString;
        public string searchString
        {
            get
            {
                return m_searchString;
            }
            set
            {
                m_searchString = value;
                if (!String.IsNullOrEmpty(value))
                    PerformRecursiveSearch(0, Inventory.RootFolder.UUID);
            }
        }

        void PerformRecursiveSearch(int level, UUID folderID)
        {
            var me = Inventory.Items[folderID].Data;
            var sorted = Inventory.GetContents(folderID);

            sorted.Sort((InventoryBase b1, InventoryBase b2) =>
            {
                if (b1 is InventoryFolder && !(b2 is InventoryFolder))
                {
                    return -1;
                }
                else if (!(b1 is InventoryFolder) && b2 is InventoryFolder)
                {
                    return 1;
                }
                else
                {
                    return string.Compare(b1.Name, b2.Name);
                }
            });

            foreach (var item in sorted)
            {
                if (item is InventoryFolder)
                {
                    PerformRecursiveSearch(level + 1, item.UUID);
                }
                else
                {
                    var it = item as InventoryItem;

                    if (it.UUID.ToString().Contains(searchString))
                        searchRes.Add(it);
                }
            }
        }

        class DeliveryQueue
        {
            public string ClassName { get; set; }
            public string id { get; set; }
            public string userUUID { get; set; }
            public string objectUUID { get; set; }
            public int price { get; set; }
            public int nextperm { get; set; }
            public string created { get; set; }
            public string delivered { get; set; }
            
        }

        private string RequestVendor(string action)
		{
			return RequestVendor(action, null);
		}
		
        private string RequestVendor(string action, Dictionary<string, string> param)
        {
            try
            {
                var webRequest = WebRequest.Create(this.vendURL);

                string postData = "action=" + action;
                if (param != null && param.Count > 0)
                {
                    var kv = param.Select(p => "&" + p.Key + "=" + p.Value);
                    postData += String.Join("", kv.ToArray());
                }
                byte[] byteArray = Encoding.UTF8.GetBytes(postData);

                webRequest.Method = "POST";
                webRequest.ContentType = "application/x-www-form-urlencoded";
                webRequest.ContentLength = byteArray.Length;

                // add post data to request
                Stream postStream = webRequest.GetRequestStream();
                postStream.Write(byteArray, 0, byteArray.Length);
                postStream.Flush();
                postStream.Close();

                using (var response = webRequest.GetResponse())
                using (var content = response.GetResponseStream())
                using (var reader = new System.IO.StreamReader(content))
                {
                    return reader.ReadToEnd();
                }
            }
            catch { }
            return null;
        }

        private List<DeliveryQueue> parseResponse(string content)
        {
            List<DeliveryQueue> queue = new List<DeliveryQueue>();

            if (String.IsNullOrEmpty(content)) return queue;

            try
            {
                System.Reflection.PropertyInfo[] propertyInfos = typeof(DeliveryQueue).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                string field_separator = "|";

                var lines = content.Split("\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                foreach (string l in lines)
                {
                    int lastPos = 0;

                    var deliveryQ = new DeliveryQueue();
                    foreach (System.Reflection.PropertyInfo pInfo in propertyInfos)
                    {
                        var nextPos = l.IndexOf(field_separator, lastPos);
                        if (nextPos > -1)
                        {
                            object o = Convert.ChangeType(l.Substring(lastPos, nextPos - lastPos), pInfo.PropertyType);
                            pInfo.SetValue(deliveryQ, o, null);
                        }
                        lastPos = nextPos + 1;
                    }

                    queue.Add(deliveryQ);
                }
            }
            catch (Exception ex)
            {
                instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": Failed to read DeliveryQ -  " + ex.Message, ChatBufferTextStyle.Error);
            }
            return queue;
        }

        private bool SendObject(DeliveryQueue p)
        {
            searchRes.Clear();
            searchString = p.objectUUID;
            if (searchRes.Count <= 0)
            {
                instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": Product not found '" + searchString + "' for user '" + p.userUUID + "'", ChatBufferTextStyle.Error);
                return false;
            }
            if (searchRes.Count > 1)
            {
                instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": More then one product found for '" + searchString + "'", ChatBufferTextStyle.Error);
                return false;
            }

            var inv = searchRes[0] as InventoryItem;
            if (inv == null)
            {
                instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": Product found, but not an inventory item", ChatBufferTextStyle.Error);
                return false;
            }


            Dictionary<string, string> param = new Dictionary<string, string>();
            param.Add("id", p.id);
            var str = this.RequestVendor("SETDELIVERED", param);
            if (str != "Delivered|1")
            {
                instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": Product found, but user " + p.userUUID + " might not have enough funds", ChatBufferTextStyle.Normal);
                // a message to the user would be helpful later
                return false;
            }
            instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": SETDELIVERED: " + str, ChatBufferTextStyle.StatusBlue);

            inv.Permissions.NextOwnerMask = (PermissionMask)p.nextperm;
            client.Inventory.RequestUpdateItem(inv);
            client.Inventory.RequestFetchInventory(inv.UUID, inv.OwnerID);

            Manager.GiveItem(inv.UUID, inv.Name, inv.AssetType, LibreMetaverse.UUID.Parse(p.userUUID), false);
            instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": PRODUCT '" + searchRes[0].Name + "' SENT TO " + p.userUUID, ChatBufferTextStyle.StatusBlue);

            return true;
        }

        private bool isSending = false;
        private void productCallback(object obj)
        {
            Manager = client.Inventory;
            Inventory = Manager.Store;
            if (Inventory == null)
            {
                instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": Waiting for Inventory...");
                return;
            }

            if (isSending == true)
            {
                instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": Waiting...");
                return;
            }
            isSending = true;
            
            Inventory.RootFolder.OwnerID = client.Self.AgentID;
            
            Dictionary<string, string> param = new Dictionary<string, string>();
            param.Add("botUUID", client.Self.AgentID.ToString());
            var strContent = this.RequestVendor("GETOUTSTANDING", param);
            List<DeliveryQueue> queue = this.parseResponse(strContent);
            instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": " + queue.Count + " Items in Queue List");

            // check if something neddc to be done
            if (queue.Count > 0)
            {
                foreach (DeliveryQueue p in queue)
                    this.SendObject(p);
            }

            isSending = false;
        }

        private void numStartupDelay_ValueChanged(object sender, EventArgs e)
        {
            this.SetupTimer();
        }

        private void numDeliveryInterval_ValueChanged(object sender, EventArgs e)
        {
            this.SetupTimer();
        }
    }
}
    
