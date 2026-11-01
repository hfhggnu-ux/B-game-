using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace TomYum
{
    // ---------- small world components ----------

    public class ItemObj : MonoBehaviour
    {
        public int id;
        public ItemType type;
        public bool chopped;
        public int chop;
        public int holder = -1;
        public bool filled;
        public List<SoupEntry> soup = new List<SoupEntry>();
        public float soupTemp;
        public Transform vis;
        public GameObject soupVis;
        public Rigidbody rb;
        public Collider col;
        public Vector3 netPos;
        public Quaternion netRot = Quaternion.identity;

        public void SetChopped(bool v)
        {
            chopped = v;
            if (vis != null) vis.localScale = v ? new Vector3(1.15f, 0.55f, 1.15f) : Vector3.one;
        }

        public void SetFilled(bool v)
        {
            filled = v;
            if (soupVis != null) soupVis.SetActive(v);
        }
    }

    public class Bin : MonoBehaviour { public ItemType type; }

    public enum StationKind { Stove, Pot, Board, Serve }

    public class Station : MonoBehaviour { public StationKind kind; }

    public class PlayerAvatar : MonoBehaviour { public int id; public string pname = ""; }

    public class PState
    {
        public int id;
        public string name = "Chef";
        public Vector3 pos;
        public float yaw, pitch;
        public int held = -1;
    }

    public class Order
    {
        public int id;
        public int spice;
        public float left;
        public int state;   // 0 = waiting for food, 1 = eating
        public float eat;   // seconds of eating left
        public int pay;     // paid when finished eating
    }

    // ---------- the game ----------

    public class GameManager : MonoBehaviour
    {
        static GameManager inst;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst != null) return;
            var g = new GameObject("TomYumGame");
            inst = g.AddComponent<GameManager>();
        }

        enum Mode { Title, Connecting, Playing }

        Mode mode = Mode.Title;
        bool menuOpen = true;
        NetPeer net;
        bool isHost;
        int myId;
        bool worldReady;

        string playerName = "Chef";
        string ipText = "192.168.1.";
        string portText = "7777";
        string status = "";
        string localIp = "";
        string publicIp = "";
        Vector2 menuScroll;

        readonly Dictionary<int, ItemObj> items = new Dictionary<int, ItemObj>();
        readonly Dictionary<int, PState> players = new Dictionary<int, PState>();
        readonly Dictionary<int, PlayerAvatar> avatars = new Dictionary<int, PlayerAvatar>();
        readonly List<Order> orders = new List<Order>();
        readonly List<KeyValuePair<string, float>> toasts = new List<KeyValuePair<string, float>>();
        readonly List<KeyValuePair<Vector3, string>> labels = new List<KeyValuePair<Vector3, string>>();
        readonly Collider[] buf = new Collider[64];

        int nextItemId = 1, orderSeq;
        float nextOrder, snapT, posT;

        int money;
        bool potFire;
        int potWater;
        float potTemp = 25f;
        List<SoupEntry> potEntries = new List<SoupEntry>();

        LocalPlayer local;
        Camera cam;
        Transform potT, boardT, serveT;
        Vector3 potSize = Vector3.one;
        GameObject fireGO, waterGO;
        Renderer waterRend;

        class Cust { public GameObject go; public int slot; public bool leaving; public float halfH; public GameObject bowl; }
        readonly Dictionary<int, Cust> customers = new Dictionary<int, Cust>();
        readonly bool[] slotUsed = new bool[4];
        static readonly Vector3[] Slots =
        {
            new Vector3(1.6f, 0f, -2.6f), new Vector3(2.8f, 0f, -2.6f),
            new Vector3(4.0f, 0f, -2.6f), new Vector3(5.2f, 0f, -2.6f)
        };
        static readonly Vector3 Entrance = new Vector3(5.4f, 0f, -4.5f);
        const float CustomerYawOffset = 0f; // set to 180 if customers face away from the counter

        GUIStyle sLabel, sCenter, sBtn, sBig, sField;
        float styleScale = -1f;
        Texture2D circleTex;

        static readonly Vector3 Spawn = new Vector3(0f, 0.1f, 1.5f);

        // ======================= setup =======================

        void Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            localIp = GetLocalIp();
            circleTex = MakeCircle(64);
            BuildWorld();
            BuildDecor();
            BuildLocal();
        }

        static Texture2D MakeCircle(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            float r = n / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, d < r - 1f ? 1f : 0f));
                }
            t.Apply();
            return t;
        }

        GameObject Box(string name, Vector3 pos, Vector3 size, Color c, bool collide = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.position = pos;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().material.color = c;
            if (!collide) Destroy(g.GetComponent<Collider>());
            return g;
        }

        // Loads a Blender model (Resources/Models/<name>.fbx), rescales it so its longest side is
        // maxSize metres and centres it on the returned holder. Falls back to a coloured primitive.
        GameObject LoadVisual(string modelName, float maxSize, Color fallback, PrimitiveType prim, out Vector3 size)
        {
            var holder = new GameObject("Vis");
            GameObject prefab = Resources.Load<GameObject>("Models/" + modelName);
            GameObject m;
            if (prefab != null)
            {
                m = Instantiate(prefab);
            }
            else
            {
                m = GameObject.CreatePrimitive(prim);
                m.GetComponent<Renderer>().material.color = fallback;
            }
            m.transform.SetParent(holder.transform, false);
            foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);

            var rends = m.GetComponentsInChildren<Renderer>();
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            float mx = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float s = mx > 0.0001f ? maxSize / mx : 1f;
            m.transform.localScale = m.transform.localScale * s;

            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            m.transform.position -= b.center - holder.transform.position;
            size = b.size;
            return holder;
        }

        void BuildWorld()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.0f;
            sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            var wall = new Color(0.93f, 0.9f, 0.8f);
            var wood = new Color(0.55f, 0.38f, 0.22f);
            var steel = new Color(0.7f, 0.7f, 0.74f);

            Box("Floor", new Vector3(0, -0.05f, 0), new Vector3(12, 0.1f, 10), new Color(0.75f, 0.72f, 0.68f));
            Box("WallN", new Vector3(0, 1.5f, 5.05f), new Vector3(12, 3, 0.1f), wall);
            Box("WallS", new Vector3(0, 1.5f, -5.05f), new Vector3(12, 3, 0.1f), wall);
            Box("WallE", new Vector3(6.05f, 1.5f, 0), new Vector3(0.1f, 3, 10), wall);
            Box("WallW", new Vector3(-6.05f, 1.5f, 0), new Vector3(0.1f, 3, 10), wall);

            // ingredient bins along the back wall
            Box("BinCounter", new Vector3(0, 0.45f, 4.4f), new Vector3(10.4f, 0.9f, 1.0f), wood);
            for (int i = 0; i < 10; i++)
            {
                var t = (ItemType)i;
                Vector3 p = new Vector3(-4.5f + i, 1.05f, 4.4f);
                var g = Box("Bin_" + Defs.Names[i], p, new Vector3(0.75f, 0.3f, 0.75f), Defs.Colors[i] * 0.85f);
                g.AddComponent<Bin>().type = t;
                labels.Add(new KeyValuePair<Vector3, string>(p + Vector3.up * 0.4f, Defs.Names[i]));
            }

            // stove + pot
            var stove = Box("Stove", new Vector3(-3f, 0.45f, -1f), new Vector3(1.5f, 0.9f, 1.3f), steel);
            stove.AddComponent<Station>().kind = StationKind.Stove;
            labels.Add(new KeyValuePair<Vector3, string>(new Vector3(-3f, 0.95f, -1.9f), "Stove"));

            fireGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fireGO.name = "Fire";
            Destroy(fireGO.GetComponent<Collider>());
            fireGO.transform.position = new Vector3(-3f, 0.93f, -1f);
            fireGO.transform.localScale = new Vector3(0.8f, 0.03f, 0.8f);
            fireGO.GetComponent<Renderer>().material.color = new Color(1f, 0.45f, 0.05f);
            fireGO.SetActive(false);

            potT = new GameObject("Pot").transform;
            Vector3 psize;
            var potVis = LoadVisual("pot", 1.3f, new Color(0.6f, 0.6f, 0.65f), PrimitiveType.Cylinder, out psize);
            potSize = psize;
            potVis.transform.SetParent(potT, false);
            potT.position = new Vector3(-3f, 0.97f + psize.y / 2f, -1f);
            var pc = potT.gameObject.AddComponent<BoxCollider>();
            pc.size = psize;
            pc.isTrigger = true; // items fall in; rays still hit it
            potT.gameObject.AddComponent<Station>().kind = StationKind.Pot;

            waterGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            waterGO.name = "Water";
            Destroy(waterGO.GetComponent<Collider>());
            waterGO.transform.SetParent(potT, false);
            waterRend = waterGO.GetComponent<Renderer>();
            waterGO.SetActive(false);

            // cutting board
            Box("BoardCounter", new Vector3(0f, 0.45f, -1f), new Vector3(1.8f, 0.9f, 1.0f), wood);
            var board = Box("Board", new Vector3(0f, 0.93f, -1f), new Vector3(1.3f, 0.06f, 0.75f), new Color(0.9f, 0.8f, 0.6f));
            board.AddComponent<Station>().kind = StationKind.Board;
            boardT = board.transform;
            labels.Add(new KeyValuePair<Vector3, string>(new Vector3(0f, 1.2f, -1.55f), "Cutting board"));

            // serving counter
            Box("ServeCounter", new Vector3(3.5f, 0.45f, -1f), new Vector3(1.8f, 0.9f, 1.3f), wood);
            var pad = Box("ServePad", new Vector3(3.5f, 0.93f, -1f), new Vector3(1.3f, 0.06f, 0.9f), new Color(0.3f, 0.7f, 0.35f));
            pad.AddComponent<Station>().kind = StationKind.Serve;
            serveT = pad.transform;
            labels.Add(new KeyValuePair<Vector3, string>(new Vector3(3.5f, 1.2f, -1.7f), "Serve here"));
        }

        // placed on the floor at floorPos, rotated by yaw
        GameObject Decor(string model, float maxSize, Color fallback, Vector3 floorPos, float yaw, bool collide)
        {
            Vector3 size;
            var g = LoadVisual(model, maxSize, fallback, PrimitiveType.Cube, out size);
            g.name = "Decor_" + model;
            g.transform.position = new Vector3(floorPos.x, floorPos.y + size.y / 2f, floorPos.z);
            g.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (collide)
            {
                var bc = g.AddComponent<BoxCollider>();
                bc.size = size;
            }
            return g;
        }

        void BuildDecor()
        {
            // dining tables + stools
            Decor("table", 1.1f, new Color(0.6f, 0.4f, 0.22f), new Vector3(-4.2f, 0f, -3.2f), 0f, true);
            Decor("table", 1.1f, new Color(0.6f, 0.4f, 0.22f), new Vector3(-1.8f, 0f, -3.4f), 0f, true);
            Vector3[] stools =
            {
                new Vector3(-4.95f, 0f, -3.2f), new Vector3(-3.45f, 0f, -3.2f),
                new Vector3(-2.55f, 0f, -3.4f), new Vector3(-1.05f, 0f, -3.4f)
            };
            foreach (var sp in stools) Decor("stool", 0.55f, new Color(0.5f, 0.33f, 0.18f), sp, 0f, true);

            // plants in the corners
            Decor("plant", 1.0f, new Color(0.15f, 0.5f, 0.2f), new Vector3(-5.65f, 0f, 4.4f), 0f, true);
            Decor("plant", 1.0f, new Color(0.15f, 0.5f, 0.2f), new Vector3(5.65f, 0f, 4.4f), 0f, true);
            Decor("plant", 1.0f, new Color(0.15f, 0.5f, 0.2f), new Vector3(-5.6f, 0f, -4.6f), 0f, true);

            // hanging red lanterns
            for (int i = -2; i <= 2; i++)
                Decor("lantern", 0.55f, new Color(0.85f, 0.08f, 0.08f), new Vector3(i * 2f, 2.1f, 0.5f), i * 20f, false);

            // spice shelves on the side walls
            Decor("shelf", 1.4f, new Color(0.5f, 0.35f, 0.2f), new Vector3(-5.85f, 1.5f, 1.5f), 90f, false);
            Decor("shelf", 1.4f, new Color(0.5f, 0.35f, 0.2f), new Vector3(5.85f, 1.6f, 1.5f), 90f, false);

            // extra prep counter against the west wall
            Decor("counter", 1.8f, new Color(0.55f, 0.38f, 0.22f), new Vector3(-5.1f, 0f, -1f), 90f, true);

            // shop sign and entrance door on the south wall
            Decor("sign", 2.4f, new Color(0.45f, 0.05f, 0.05f), new Vector3(0f, 2.0f, -4.93f), 0f, false);
            labels.Add(new KeyValuePair<Vector3, string>(new Vector3(0f, 2.65f, -4.8f), "TOM YUM KITCHEN"));
            Box("Door", new Vector3(5.4f, 1.0f, -4.98f), new Vector3(1.1f, 2.0f, 0.04f), new Color(0.4f, 0.25f, 0.12f), false);
        }

        void BuildLocal()
        {
            var go = new GameObject("LocalPlayer");
            go.layer = 2; // Ignore Raycast: other players' rays pass through us
            go.transform.position = Spawn;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var camGO = new GameObject("PlayerCamera");
            camGO.transform.SetParent(go.transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            cam = camGO.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.75f, 0.9f);
            camGO.AddComponent<AudioListener>();

            local = go.AddComponent<LocalPlayer>();
            local.gm = this;
            local.cc = cc;
            local.cam = camGO.transform;
        }

        // ======================= items =======================

        ItemObj MakeItem(int id, ItemType t, Vector3 pos, bool physics)
        {
            Vector3 size;
            var prim = (t == ItemType.Bowl || t == ItemType.FishSauce) ? PrimitiveType.Cylinder : PrimitiveType.Sphere;
            var vis = LoadVisual(Defs.Models[(int)t], Defs.Sizes[(int)t], Defs.Colors[(int)t], prim, out size);

            var go = new GameObject("Item_" + Defs.Names[(int)t]);
            go.transform.position = pos;
            vis.transform.SetParent(go.transform, false);

            var it = go.AddComponent<ItemObj>();
            it.id = id;
            it.type = t;
            it.vis = vis.transform;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            it.col = bc;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.isKinematic = !physics;
            it.rb = rb;
            it.netPos = pos;
            it.netRot = go.transform.rotation;

            if (t == ItemType.Bowl)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(d.GetComponent<Collider>());
                d.transform.SetParent(go.transform, false);
                d.transform.localPosition = new Vector3(0f, size.y * 0.15f, 0f);
                d.transform.localScale = new Vector3(size.x * 0.75f, 0.01f, size.z * 0.75f);
                d.GetComponent<Renderer>().material.color = new Color(0.9f, 0.5f, 0.1f);
                d.SetActive(false);
                it.soupVis = d;
            }

            items[id] = it;
            return it;
        }

        void DestroyItem(ItemObj it)
        {
            foreach (var ps in players.Values) if (ps.held == it.id) ps.held = -1;
            items.Remove(it.id);
            Destroy(it.gameObject);
        }

        void ClearItems()
        {
            foreach (var it in items.Values) if (it != null) Destroy(it.gameObject);
            items.Clear();
        }

        // ======================= main loop =======================

        void Update()
        {
            float dt = Time.deltaTime;

            if (!Application.isMobilePlatform)
            {
                if (Input.GetKeyDown(KeyCode.Escape) && mode == Mode.Playing) menuOpen = !menuOpen;
                bool lockIt = mode == Mode.Playing && !menuOpen;
                Cursor.lockState = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !lockIt;
            }

            if (mode == Mode.Connecting)
            {
                if (net.State == NetState.Connected)
                {
                    net.Send(0, "H|" + Defs.Clean(playerName));
                    isHost = false;
                    worldReady = false;
                    ClearItems();
                    players.Clear();
                    mode = Mode.Playing;
                    menuOpen = false;
                    status = "";
                }
                else if (net.State == NetState.Failed)
                {
                    status = "Could not connect: " + net.Error;
                    net.Stop();
                    net = null;
                    mode = Mode.Title;
                }
                return;
            }

            if (mode != Mode.Playing || net == null) { UpdateVisuals(dt); return; }

            if (isHost) HostTick(dt); else ClientTick(dt);
            UpdateVisuals(dt);
        }

        void HostTick(float dt)
        {
            int jid;
            while (net.TryJoined(out jid)) { /* they introduce themselves with a HELLO line */ }
            while (net.TryLeft(out jid)) HostRemovePlayer(jid);
            NetPeer.Msg m;
            while (net.TryGetMsg(out m)) HostHandle(m.from, m.text);

            PState me = players[0];
            me.pos = local.transform.position;
            me.yaw = local.yaw;
            me.pitch = local.pitch;

            foreach (var ps in players.Values)
            {
                if (ps.held < 0) continue;
                ItemObj it;
                if (items.TryGetValue(ps.held, out it)) PlaceHeld(it, ps); else ps.held = -1;
            }

            SimPot(dt);
            SimOrders(dt);
            CheckZones();
            CullFallen();

            snapT -= dt;
            if (snapT <= 0f)
            {
                snapT = 1f / 12f;
                net.Broadcast(BuildSnapshot());
            }
        }

        void ClientTick(float dt)
        {
            NetPeer.Msg m;
            while (net.TryGetMsg(out m)) ClientHandle(m.text);
            int lid;
            while (net.TryLeft(out lid))
            {
                Leave("Disconnected from host.");
                return;
            }
            posT -= dt;
            if (posT <= 0f && worldReady)
            {
                posT = 1f / 15f;
                net.Send(0, "P|" + V(local.transform.position) + "|" + Defs.F(local.yaw) + "|" + Defs.F(local.pitch));
            }
        }

        // ======================= message helpers =======================

        static string V(Vector3 v) { return Defs.F(v.x) + "|" + Defs.F(v.y) + "|" + Defs.F(v.z); }

        static Vector3 Vec(string[] f, int i)
        {
            return new Vector3(Defs.P(f[i]), Defs.P(f[i + 1]), Defs.P(f[i + 2]));
        }

        void Toast(string s)
        {
            AddToast(s);
            if (isHost && net != null) net.Broadcast("M|" + s.Replace('|', ' '));
        }

        void AddToast(string s)
        {
            toasts.Add(new KeyValuePair<string, float>(s, Time.time + 5f));
            while (toasts.Count > 4) toasts.RemoveAt(0);
        }

        // ======================= host: handling requests =======================

        void HostHandle(int from, string text)
        {
            var f = text.Split('|');
            PState ps;
            switch (f[0])
            {
                case "H":
                    if (!players.TryGetValue(from, out ps)) { ps = new PState { id = from }; players[from] = ps; }
                    ps.name = f.Length > 1 ? Defs.Clean(f[1]) : "Chef";
                    if (ps.name.Length == 0) ps.name = "Chef " + from;
                    ps.pos = Spawn;
                    net.Send(from, "W|" + from);
                    Toast(ps.name + " joined the kitchen");
                    break;
                case "P":
                    if (players.TryGetValue(from, out ps) && f.Length >= 6)
                    {
                        ps.pos = Vec(f, 1);
                        ps.yaw = Defs.P(f[4]);
                        ps.pitch = Defs.P(f[5]);
                    }
                    break;
                case "G":
                    if (players.ContainsKey(from) && f.Length >= 7) HostGrab(from, Vec(f, 1), Vec(f, 4));
                    break;
                case "U":
                    if (players.ContainsKey(from) && f.Length >= 7) HostUse(from, Vec(f, 1), Vec(f, 4));
                    break;
                case "T":
                    if (players.ContainsKey(from) && f.Length >= 4) HostThrow(from, Vec(f, 1));
                    break;
            }
        }

        void HostRemovePlayer(int id)
        {
            PState ps;
            if (!players.TryGetValue(id, out ps)) return;
            if (ps.held >= 0) Drop(id, false, Vector3.forward);
            players.Remove(id);
            Toast(ps.name + " left");
        }

        void ClientHandle(string text)
        {
            var f = text.Split('|');
            switch (f[0])
            {
                case "W": myId = Defs.PI(f[1]); worldReady = true; break;
                case "S": ApplySnapshot(f); break;
                case "M": if (f.Length > 1) AddToast(f[1]); break;
            }
        }

        // ======================= host: actions =======================

        void PlaceHeld(ItemObj it, PState ps)
        {
            Vector3 eye = ps.pos + Vector3.up * 1.6f;
            it.transform.position = eye + Quaternion.Euler(ps.pitch, ps.yaw, 0f) * new Vector3(0.25f, -0.3f, 0.9f);
            it.transform.rotation = Quaternion.Euler(0f, ps.yaw, 0f);
        }

        void Pick(int pid, ItemObj it)
        {
            players[pid].held = it.id;
            it.holder = pid;
            it.rb.isKinematic = true;
            it.col.enabled = false;
        }

        void Drop(int pid, bool thrown, Vector3 dir)
        {
            var ps = players[pid];
            ItemObj it;
            if (ps.held < 0) return;
            if (!items.TryGetValue(ps.held, out it)) { ps.held = -1; return; }
            ps.held = -1;
            it.holder = -1;
            it.col.enabled = true;
            it.rb.isKinematic = false;
            Vector3 push = thrown ? dir.normalized * 7f + Vector3.up * 1.5f : dir.normalized * 0.8f;
            it.rb.AddForce(push, ForceMode.VelocityChange);
        }

        void HostGrab(int pid, Vector3 o, Vector3 d)
        {
            var ps = players[pid];
            if (ps.held >= 0) { Drop(pid, false, d); return; }

            RaycastHit h;
            if (!Physics.Raycast(o, d, out h, 3.2f)) return;

            var item = h.collider.GetComponentInParent<ItemObj>();
            if (item != null && item.holder < 0) { Pick(pid, item); return; }

            var bin = h.collider.GetComponentInParent<Bin>();
            if (bin != null)
            {
                var it = MakeItem(nextItemId++, bin.type, bin.transform.position + Vector3.up * 0.5f, true);
                Pick(pid, it);
            }
        }

        void HostThrow(int pid, Vector3 d)
        {
            if (players[pid].held >= 0) Drop(pid, true, d);
        }

        void HostUse(int pid, Vector3 o, Vector3 d)
        {
            var ps = players[pid];
            ItemObj held = null;
            if (ps.held >= 0) items.TryGetValue(ps.held, out held);

            RaycastHit h;
            if (!Physics.Raycast(o, d, out h, 3.2f)) return;
            var st = h.collider.GetComponentInParent<Station>();
            if (st == null) return;

            switch (st.kind)
            {
                case StationKind.Stove:
                    potFire = !potFire;
                    Toast(potFire ? "Stove ON" : "Stove off");
                    break;

                case StationKind.Pot:
                    if (held == null)
                    {
                        if (potWater < 4)
                        {
                            potWater++;
                            potTemp = Mathf.Max(25f, potTemp - 10f);
                            Toast("Poured water (" + potWater + "/4)");
                        }
                        else Toast("The pot is full");
                    }
                    else if (held.type == ItemType.Bowl) Ladle(held);
                    else
                    {
                        AddToPot(held.type, held.chopped);
                        DestroyItem(held);
                    }
                    break;

                case StationKind.Board:
                    ChopAt(held);
                    break;

                case StationKind.Serve:
                    if (held != null && held.type == ItemType.Bowl && held.filled) Serve(held);
                    else Toast("Bring a bowl of soup here");
                    break;
            }
        }

        void ChopAt(ItemObj held)
        {
            ItemObj target = null;
            if (held != null && Defs.Choppable(held.type) && !held.chopped) target = held;
            else
            {
                int n = Physics.OverlapBoxNonAlloc(boardT.position + Vector3.up * 0.15f, new Vector3(0.6f, 0.15f, 0.35f), buf);
                for (int i = 0; i < n; i++)
                {
                    var it = buf[i].GetComponentInParent<ItemObj>();
                    if (it != null && it.holder < 0 && Defs.Choppable(it.type) && !it.chopped) { target = it; break; }
                }
            }
            if (target == null) { Toast("Put something choppable on the board (or hold it)"); return; }

            target.chop++;
            if (target.chop >= Defs.ChopsNeeded)
            {
                target.SetChopped(true);
                Toast("Chopped " + Defs.Names[(int)target.type]);
            }
            else Toast("Chop " + target.chop + "/" + Defs.ChopsNeeded);
        }

        void AddToPot(ItemType t, bool chopped)
        {
            foreach (var e in potEntries)
            {
                if (e.type == t && e.chopped == chopped)
                {
                    e.cook = e.cook * e.count / (e.count + 1f); // new item is raw: average the cooking
                    e.count++;
                    Toast(Defs.Names[(int)t] + " into the pot");
                    return;
                }
            }
            potEntries.Add(new SoupEntry { type = t, chopped = chopped, count = 1, cook = 0f });
            Toast(Defs.Names[(int)t] + " into the pot");
        }

        void Ladle(ItemObj bowl)
        {
            if (bowl.filled) { Toast("The bowl is already full"); return; }
            if (potWater <= 0) { Toast("The pot is empty"); return; }

            var taken = new List<SoupEntry>();
            foreach (var e in potEntries)
            {
                int take = Mathf.CeilToInt(e.count / (float)potWater);
                take = Mathf.Min(take, e.count);
                if (take <= 0) continue;
                var c = e.Clone();
                c.count = take;
                taken.Add(c);
                e.count -= take;
            }
            potEntries.RemoveAll(e => e.count <= 0);
            bowl.soup = taken;
            bowl.soupTemp = potTemp;
            bowl.SetFilled(true);
            potWater--;
            if (potWater == 0) potEntries.Clear();
            Toast("Ladled a bowl of soup");
        }

        static int Score(List<SoupEntry> e, float temp, int spice)
        {
            int Cnt(ItemType t, bool needChop, bool needCook)
            {
                int c = 0;
                foreach (var x in e)
                {
                    if (x.type != t) continue;
                    if (needChop && !x.chopped) continue;
                    if (needCook && !(x.cook >= Defs.CookTime && x.cook < Defs.BurntTime)) continue;
                    c += x.count;
                }
                return c;
            }

            int s = 0;
            s += Cnt(ItemType.Shrimp, false, true) > 0 ? 15 : (Cnt(ItemType.Shrimp, false, false) > 0 ? 3 : 0);
            s += Cnt(ItemType.Mushroom, true, true) > 0 ? 10 : (Cnt(ItemType.Mushroom, false, false) > 0 ? 4 : 0);
            s += Cnt(ItemType.Lemongrass, true, false) > 0 ? 10 : (Cnt(ItemType.Lemongrass, false, false) > 0 ? 4 : 0);
            s += Cnt(ItemType.Galangal, true, false) > 0 ? 10 : (Cnt(ItemType.Galangal, false, false) > 0 ? 4 : 0);
            s += Cnt(ItemType.KaffirLeaf, false, false) > 0 ? 8 : 0;
            s += Cnt(ItemType.Lime, false, false) > 0 ? 8 : 0;
            s += Cnt(ItemType.FishSauce, false, false) > 0 ? 8 : 0;
            s += Cnt(ItemType.Tomato, false, false) > 0 ? 5 : 0;

            int chiliChopped = Cnt(ItemType.Chili, true, false);
            int chiliWhole = Cnt(ItemType.Chili, false, false) - chiliChopped;
            s += Mathf.Max(0, 16 - 6 * Mathf.Abs(chiliChopped - spice) - 3 * chiliWhole);

            if (temp >= 70f) s += 10;
            foreach (var x in e) if (x.cook >= Defs.BurntTime) { s -= 30; break; }
            return Mathf.Clamp(s, 0, 100);
        }

        void Serve(ItemObj bowl)
        {
            Order best = null;
            int bestScore = -1;
            foreach (var o in orders)
            {
                if (o.state != 0) continue;
                int sc = Score(bowl.soup, bowl.soupTemp, o.spice);
                if (sc > bestScore) { bestScore = sc; best = o; }
            }

            int pay;
            string msg;
            if (best == null)
            {
                bestScore = Score(bowl.soup, bowl.soupTemp, 1);
                pay = bestScore / 4;
                msg = "No order waiting - sold cheap for $" + pay;
            }
            else
            {
                pay = bestScore >= 40 ? bestScore + Mathf.RoundToInt(best.left / 10f) : 0;
                if (pay > 0)
                {
                    best.state = 1;
                    best.eat = 6f;
                    best.pay = pay;
                    msg = "Order #" + best.id + " served! Score " + bestScore + "/100 - the customer is eating";
                }
                else
                {
                    orders.Remove(best);
                    msg = "Customer refused order #" + best.id + " (score " + bestScore + ")";
                }
            }
            if (best == null) money += pay; // cheap sale with no order is paid at once
            Toast(msg);
            DestroyItem(bowl);
        }

        // ======================= host: simulation =======================

        void SimPot(float dt)
        {
            if (potFire)
            {
                if (potWater > 0) potTemp = Mathf.Min(100f, potTemp + 7f * dt);
                else
                {
                    potTemp = Mathf.Min(220f, potTemp + 25f * dt);
                    foreach (var e in potEntries) e.cook += 3f * dt; // dry pot burns things
                }
            }
            else potTemp = Mathf.Max(25f, potTemp - 3f * dt);

            if (potWater > 0 && potTemp >= 90f)
                foreach (var e in potEntries) e.cook += dt;
        }

        void SimOrders(float dt)
        {
            for (int i = orders.Count - 1; i >= 0; i--)
            {
                var od = orders[i];
                if (od.state == 1)
                {
                    od.eat -= dt;
                    if (od.eat <= 0f)
                    {
                        money += od.pay;
                        Toast("Customer #" + od.id + " finished and paid $" + od.pay);
                        orders.RemoveAt(i);
                    }
                    continue;
                }
                od.left -= dt;
                if (od.left <= 0f)
                {
                    Toast("Order #" + od.id + " expired");
                    orders.RemoveAt(i);
                }
            }
            nextOrder -= dt;
            if (nextOrder <= 0f && orders.Count < 4)
            {
                orders.Add(new Order { id = ++orderSeq, spice = UnityEngine.Random.Range(0, 4), left = 150f });
                nextOrder = UnityEngine.Random.Range(18f, 30f);
                Toast("New order #" + orderSeq);
            }
        }

        void CheckZones()
        {
            var consumed = new List<ItemObj>();
            int n = Physics.OverlapBoxNonAlloc(potT.position, new Vector3(0.4f, potSize.y * 0.5f, 0.4f), buf);
            for (int i = 0; i < n; i++)
            {
                var it = buf[i].GetComponentInParent<ItemObj>();
                if (it != null && it.holder < 0 && it.type != ItemType.Bowl && !consumed.Contains(it)) consumed.Add(it);
            }
            foreach (var it in consumed)
            {
                AddToPot(it.type, it.chopped);
                DestroyItem(it);
            }

            ItemObj bowl = null;
            n = Physics.OverlapBoxNonAlloc(serveT.position + Vector3.up * 0.2f, new Vector3(0.6f, 0.25f, 0.4f), buf);
            for (int i = 0; i < n; i++)
            {
                var it = buf[i].GetComponentInParent<ItemObj>();
                if (it != null && it.holder < 0 && it.type == ItemType.Bowl && it.filled) { bowl = it; break; }
            }
            if (bowl != null) Serve(bowl);
        }

        void CullFallen()
        {
            List<ItemObj> dead = null;
            foreach (var it in items.Values)
                if (it.transform.position.y < -3f)
                {
                    if (dead == null) dead = new List<ItemObj>();
                    dead.Add(it);
                }
            if (dead != null) foreach (var it in dead) DestroyItem(it);
        }

        // ======================= snapshots =======================

        string BuildSnapshot()
        {
            var sb = new StringBuilder("S|");
            sb.Append(money).Append('|').Append(potFire ? 1 : 0).Append('|').Append(potWater).Append('|')
              .Append(Defs.F(potTemp)).Append('|').Append(Defs.Pack(potEntries)).Append('|');

            foreach (var o in orders)
                sb.Append(o.id).Append(',').Append(o.spice).Append(',').Append(Mathf.CeilToInt(o.left)).Append(',')
                  .Append(o.state).Append(',').Append(Mathf.CeilToInt(o.eat)).Append(';');
            sb.Append('|');

            foreach (var ps in players.Values)
                sb.Append(ps.id).Append(',').Append(Defs.F(ps.pos.x)).Append(',').Append(Defs.F(ps.pos.y)).Append(',')
                  .Append(Defs.F(ps.pos.z)).Append(',').Append(Defs.F(ps.yaw)).Append(',').Append(Defs.F(ps.pitch)).Append(',')
                  .Append(ps.name).Append(';');
            sb.Append('|');

            foreach (var it in items.Values)
            {
                Vector3 p = it.transform.position;
                Vector3 r = it.transform.eulerAngles;
                sb.Append(it.id).Append(',').Append((int)it.type).Append(',').Append(it.chopped ? 1 : 0).Append(',')
                  .Append(it.chop).Append(',').Append(it.holder).Append(',').Append(it.filled ? 1 : 0).Append(',')
                  .Append(Defs.F(p.x)).Append(',').Append(Defs.F(p.y)).Append(',').Append(Defs.F(p.z)).Append(',')
                  .Append(Defs.F(r.x)).Append(',').Append(Defs.F(r.y)).Append(',').Append(Defs.F(r.z)).Append(';');
            }
            return sb.ToString();
        }

        void ApplySnapshot(string[] f)
        {
            if (f.Length < 9) return;
            money = Defs.PI(f[1]);
            potFire = f[2] == "1";
            potWater = Defs.PI(f[3]);
            potTemp = Defs.P(f[4]);
            potEntries = Defs.Unpack(f[5]);

            orders.Clear();
            foreach (var rec in f[6].Split(';'))
            {
                if (rec.Length == 0) continue;
                var a = rec.Split(',');
                if (a.Length < 3) continue;
                orders.Add(new Order { id = Defs.PI(a[0]), spice = Defs.PI(a[1]), left = Defs.P(a[2]), state = a.Length > 3 ? Defs.PI(a[3]) : 0, eat = a.Length > 4 ? Defs.P(a[4]) : 0f });
            }

            var seenP = new HashSet<int>();
            foreach (var rec in f[7].Split(';'))
            {
                if (rec.Length == 0) continue;
                var a = rec.Split(',');
                if (a.Length < 6) continue;
                int id = Defs.PI(a[0]);
                PState ps;
                if (!players.TryGetValue(id, out ps)) { ps = new PState { id = id }; players[id] = ps; }
                ps.pos = new Vector3(Defs.P(a[1]), Defs.P(a[2]), Defs.P(a[3]));
                ps.yaw = Defs.P(a[4]);
                ps.pitch = Defs.P(a[5]);
                ps.name = a.Length > 6 && a[6].Length > 0 ? a[6] : "Chef";
                seenP.Add(id);
            }
            var rmP = new List<int>();
            foreach (var k in players.Keys) if (!seenP.Contains(k)) rmP.Add(k);
            foreach (var k in rmP) players.Remove(k);

            var seen = new HashSet<int>();
            foreach (var rec in f[8].Split(';'))
            {
                if (rec.Length == 0) continue;
                var a = rec.Split(',');
                if (a.Length < 12) continue;
                int id = Defs.PI(a[0]);
                Vector3 pos = new Vector3(Defs.P(a[6]), Defs.P(a[7]), Defs.P(a[8]));
                ItemObj it;
                if (!items.TryGetValue(id, out it))
                {
                    it = MakeItem(id, (ItemType)Defs.PI(a[1]), pos, false);
                    it.transform.position = pos;
                }
                it.netPos = pos;
                it.netRot = Quaternion.Euler(Defs.P(a[9]), Defs.P(a[10]), Defs.P(a[11]));
                it.chop = Defs.PI(a[3]);
                it.holder = Defs.PI(a[4]);
                bool chopped = a[2] == "1";
                if (it.chopped != chopped) it.SetChopped(chopped);
                bool filled = a[5] == "1";
                if (it.filled != filled) it.SetFilled(filled);
                it.col.enabled = it.holder < 0;
                seen.Add(id);
            }
            var rmI = new List<ItemObj>();
            foreach (var kv in items) if (!seen.Contains(kv.Key)) rmI.Add(kv.Value);
            foreach (var it in rmI) { items.Remove(it.id); Destroy(it.gameObject); }
        }

        // ======================= visuals =======================

        void UpdateCustomers(float dt)
        {
            if (mode != Mode.Playing)
            {
                foreach (var c in customers.Values) Destroy(c.go);
                customers.Clear();
                for (int i = 0; i < slotUsed.Length; i++) slotUsed[i] = false;
                return;
            }

            foreach (var o in orders)
            {
                if (customers.ContainsKey(o.id)) continue;
                int slot = -1;
                for (int i = 0; i < slotUsed.Length; i++) if (!slotUsed[i]) { slot = i; break; }
                if (slot < 0) continue;
                Vector3 size;
                var g = LoadVisual("customer_" + (o.id % 3), 1.65f, new Color(0.9f, 0.7f, 0.5f), PrimitiveType.Capsule, out size);
                g.name = "Customer_" + o.id;
                g.transform.position = Entrance + Vector3.up * size.y / 2f;
                slotUsed[slot] = true;
                customers[o.id] = new Cust { go = g, slot = slot, halfH = size.y / 2f };
            }

            List<int> gone = null;
            foreach (var kv in customers)
            {
                var c = kv.Value;
                int cid = kv.Key;
                if (!c.leaving && !orders.Exists(x => x.id == cid))
                {
                    c.leaving = true;
                    slotUsed[c.slot] = false;
                }
                Vector3 target = c.leaving ? Entrance : Slots[c.slot];
                Vector3 p = c.go.transform.position;
                Vector3 cur = new Vector3(p.x, 0f, p.z);
                Vector3 next = Vector3.MoveTowards(cur, target, 1.8f * dt);
                bool moving = (next - cur).sqrMagnitude > 1e-6f;
                Order eo = orders.Find(x => x.id == cid);
                bool eating = eo != null && eo.state == 1;
                if (eating && c.bowl == null)
                {
                    Vector3 bs;
                    c.bowl = LoadVisual("bowl", 0.3f, Defs.Colors[(int)ItemType.Bowl], PrimitiveType.Cylinder, out bs);
                    c.bowl.transform.SetParent(c.go.transform, false);
                    c.bowl.transform.localPosition = new Vector3(0f, 1.0f - c.halfH, 0.35f * Mathf.Cos(CustomerYawOffset * Mathf.Deg2Rad));
                    var soupD = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Destroy(soupD.GetComponent<Collider>());
                    soupD.transform.SetParent(c.bowl.transform, false);
                    soupD.transform.localPosition = new Vector3(0f, bs.y * 0.15f, 0f);
                    soupD.transform.localScale = new Vector3(bs.x * 0.75f, 0.01f, bs.z * 0.75f);
                    soupD.GetComponent<Renderer>().material.color = new Color(0.9f, 0.5f, 0.1f);
                }
                else if (!eating && c.bowl != null)
                {
                    Destroy(c.bowl);
                    c.bowl = null;
                }
                float bob = moving ? Mathf.Abs(Mathf.Sin(Time.time * 9f)) * 0.04f
                                   : (eating ? Mathf.Abs(Mathf.Sin(Time.time * 5f)) * 0.03f : 0f);
                c.go.transform.position = new Vector3(next.x, c.halfH + bob, next.z);
                float yaw = CustomerYawOffset;
                if (moving) yaw = Mathf.Atan2(target.x - next.x, target.z - next.z) * Mathf.Rad2Deg + CustomerYawOffset;
                c.go.transform.rotation = Quaternion.Slerp(c.go.transform.rotation, Quaternion.Euler(0f, yaw, 0f), 1f - Mathf.Exp(-10f * dt));
                if (c.leaving && !moving)
                {
                    if (gone == null) gone = new List<int>();
                    gone.Add(cid);
                }
            }
            if (gone != null)
                foreach (var id in gone) { Destroy(customers[id].go); customers.Remove(id); }
        }

        void UpdateVisuals(float dt)
        {
            UpdateCustomers(dt);
            if (!isHost)
            {
                float k = 1f - Mathf.Exp(-18f * dt);
                foreach (var it in items.Values)
                {
                    it.transform.position = Vector3.Lerp(it.transform.position, it.netPos, k);
                    it.transform.rotation = Quaternion.Slerp(it.transform.rotation, it.netRot, k);
                }
            }

            // avatars for every other player
            var rm = new List<int>();
            foreach (var id in avatars.Keys) if (!players.ContainsKey(id) || id == myId || mode != Mode.Playing) rm.Add(id);
            foreach (var id in rm) { Destroy(avatars[id].gameObject); avatars.Remove(id); }

            if (mode == Mode.Playing)
            {
                foreach (var ps in players.Values)
                {
                    if (ps.id == myId) continue;
                    PlayerAvatar av;
                    if (!avatars.TryGetValue(ps.id, out av)) av = MakeAvatar(ps);
                    av.pname = ps.name;
                    float k = 1f - Mathf.Exp(-14f * dt);
                    av.transform.position = Vector3.Lerp(av.transform.position, ps.pos, k);
                    av.transform.rotation = Quaternion.Slerp(av.transform.rotation, Quaternion.Euler(0f, ps.yaw, 0f), k);
                }
            }

            // pot visuals
            if (fireGO != null) fireGO.SetActive(potFire);
            if (waterGO != null)
            {
                waterGO.SetActive(potWater > 0);
                float h = 0.04f * potWater;
                waterGO.transform.localPosition = new Vector3(0f, -potSize.y / 2f + 0.03f + h, 0f);
                waterGO.transform.localScale = new Vector3(potSize.x * 0.55f, Mathf.Max(0.001f, h), potSize.z * 0.55f);
                float t = Mathf.InverseLerp(25f, 95f, potTemp);
                waterRend.material.color = Color.Lerp(new Color(0.6f, 0.8f, 0.9f), new Color(0.9f, 0.5f, 0.12f), t);
            }
        }

        PlayerAvatar MakeAvatar(PState ps)
        {
            var root = new GameObject("Avatar_" + ps.id);
            root.transform.position = ps.pos;
            Color col = Color.HSVToRGB((ps.id * 0.17f) % 1f, 0.6f, 0.9f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            body.GetComponent<Renderer>().material.color = col;

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(root.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.55f, 0.3f);
            nose.transform.localScale = new Vector3(0.2f, 0.2f, 0.3f);
            nose.GetComponent<Renderer>().material.color = Color.white;

            var av = root.AddComponent<PlayerAvatar>();
            av.id = ps.id;
            avatars[ps.id] = av;
            return av;
        }

        // ======================= local actions (buttons / keys) =======================

        public bool CanControl { get { return mode == Mode.Playing && !menuOpen && (isHost || worldReady); } }

        public void DoGrab()
        {
            if (!CanControl) return;
            Vector3 o = cam.transform.position, d = cam.transform.forward;
            if (isHost) HostGrab(0, o, d); else net.Send(0, "G|" + V(o) + "|" + V(d));
        }

        public void DoUse()
        {
            if (!CanControl) return;
            Vector3 o = cam.transform.position, d = cam.transform.forward;
            if (isHost) HostUse(0, o, d); else net.Send(0, "U|" + V(o) + "|" + V(d));
        }

        public void DoThrow()
        {
            if (!CanControl) return;
            Vector3 d = cam.transform.forward;
            if (isHost) HostThrow(0, d); else net.Send(0, "T|" + V(d));
        }

        // ======================= start / join / leave =======================

        int Port()
        {
            int p = Defs.PI(portText);
            return (p < 1024 || p > 65535) ? 7777 : p;
        }

        void StartHostGame()
        {
            net = new NetPeer();
            if (!net.StartHost(Port()))
            {
                status = "Cannot host: " + net.Error;
                net = null;
                return;
            }
            ClearItems();
            players.Clear();
            orders.Clear();
            money = 0; potFire = false; potWater = 0; potTemp = 25f; potEntries = new List<SoupEntry>();
            isHost = true;
            myId = 0;
            worldReady = true;
            string nm = Defs.Clean(playerName);
            players[0] = new PState { id = 0, name = nm.Length > 0 ? nm : "Chef", pos = Spawn };
            nextOrder = 3f;
            snapT = 0f;
            mode = Mode.Playing;
            menuOpen = false;
            status = "";
        }

        void JoinGame()
        {
            string ip = ipText.Trim();
            if (ip.Length == 0) { status = "Type the host's IP address first."; return; }
            net = new NetPeer();
            net.Connect(ip, Port());
            status = "Connecting to " + ip + " ...";
            mode = Mode.Connecting;
        }

        void Leave(string msg)
        {
            if (net != null) net.Stop();
            net = null;
            isHost = false;
            worldReady = false;
            ClearItems();
            players.Clear();
            orders.Clear();
            toasts.Clear();
            potFire = false; potWater = 0; potTemp = 25f; potEntries = new List<SoupEntry>();
            local.cc.enabled = false;
            local.transform.position = Spawn;
            local.cc.enabled = true;
            local.yaw = 180f;
            local.pitch = 0f;
            mode = Mode.Title;
            menuOpen = true;
            status = msg;
        }

        void OnApplicationQuit()
        {
            if (net != null) net.Stop();
        }

        // ======================= IP info =======================

        static string GetLocalIp()
        {
            try
            {
                // "connecting" a UDP socket sends nothing; it only lets the OS pick the outgoing interface
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("8.8.8.8", 65530);
                    return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                }
            }
            catch (System.Exception)
            {
                try
                {
                    foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                        if (a.AddressFamily == AddressFamily.InterNetwork) return a.ToString();
                }
                catch (System.Exception) { }
            }
            return "unknown (no network?)";
        }

        IEnumerator FetchPublicIp()
        {
            publicIp = "looking up...";
            using (var req = UnityWebRequest.Get("https://api.ipify.org"))
            {
                req.timeout = 8;
                yield return req.SendWebRequest();
                publicIp = req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text.Trim() : "lookup failed";
            }
        }

        // ======================= UI (IMGUI, works with touch) =======================

        float U { get { return Screen.height / 540f; } }
        Rect GrabR { get { return new Rect(Screen.width - 160f * U, Screen.height - 170f * U, 140f * U, 140f * U); } }
        Rect UseR { get { return new Rect(Screen.width - 320f * U, Screen.height - 130f * U, 140f * U, 100f * U); } }
        Rect ThrowR { get { return new Rect(Screen.width - 160f * U, Screen.height - 290f * U, 140f * U, 100f * U); } }
        Rect MenuR { get { return new Rect(Screen.width - 110f * U, 10f * U, 100f * U, 46f * U); } }

        // gp = touch position with y measured from the top
        public bool UiBlocks(Vector2 gp)
        {
            if (menuOpen) return true;
            return GrabR.Contains(gp) || UseR.Contains(gp) || ThrowR.Contains(gp) || MenuR.Contains(gp);
        }

        void EnsureStyles()
        {
            float u = U;
            if (sLabel != null && Mathf.Approximately(u, styleScale)) return;
            styleScale = u;
            sLabel = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15f * u), wordWrap = true };
            sLabel.normal.textColor = Color.white;
            sCenter = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter };
            sBig = new GUIStyle(sLabel) { fontSize = Mathf.RoundToInt(26f * u), fontStyle = FontStyle.Bold };
            sBtn = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(17f * u) };
            sField = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(17f * u) };
        }

        void DrawText(Rect r, string s, GUIStyle st)
        {
            var old = st.normal.textColor;
            st.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), s, st);
            st.normal.textColor = old;
            GUI.Label(r, s, st);
        }

        void DrawWorldLabel(Vector3 world, string text)
        {
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0.1f || sp.z > 14f) return;
            float u = U;
            DrawText(new Rect(sp.x - 100f * u, Screen.height - sp.y - 14f * u, 200f * u, 28f * u), text, sCenter);
        }

        string Hint()
        {
            if (cam == null) return "";
            RaycastHit h;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out h, 3.2f)) return "";
            var it = h.collider.GetComponentInParent<ItemObj>();
            if (it != null)
            {
                string n = Defs.Names[(int)it.type] + (it.chopped ? " (chopped)" : "") + (it.filled ? " (soup)" : "");
                return "Grab: " + n;
            }
            var b = h.collider.GetComponentInParent<Bin>();
            if (b != null) return "Take: " + Defs.Names[(int)b.type];
            var st = h.collider.GetComponentInParent<Station>();
            if (st != null)
            {
                switch (st.kind)
                {
                    case StationKind.Stove: return "USE: turn the stove on/off";
                    case StationKind.Pot: return "USE: pour water / ladle with a bowl / drop the held item in";
                    case StationKind.Board: return "USE: chop (hold the item, or leave it on the board)";
                    case StationKind.Serve: return "USE: serve the bowl (or just put it down here)";
                }
            }
            return "";
        }

        void OnGUI()
        {
            EnsureStyles();
            float u = U;

            foreach (var l in labels) DrawWorldLabel(l.Key, l.Value);
            foreach (var av in avatars.Values) DrawWorldLabel(av.transform.position + Vector3.up * 2.1f, av.pname);
            foreach (var kv in customers)
            {
                if (kv.Value.leaving) continue;
                int cid = kv.Key;
                Order od = orders.Find(x => x.id == cid);
                if (od == null) continue;
                DrawWorldLabel(kv.Value.go.transform.position + Vector3.up * (kv.Value.halfH + 0.25f),
                    od.state == 1
                        ? "Yum! eating... " + Mathf.CeilToInt(od.eat) + "s"
                        : "#" + od.id + "  " + (od.spice == 0 ? "no chili" : od.spice + " chili") + "  " + Mathf.CeilToInt(od.left) + "s");
            }

            if (mode == Mode.Playing) DrawHud(u);
            if (menuOpen || mode != Mode.Playing) DrawMenu(u);
        }

        void DrawHud(float u)
        {
            var sb = new StringBuilder();
            sb.Append("Money: $").Append(money).Append('\n');
            sb.Append("Pot: water ").Append(potWater).Append("/4, ").Append(Mathf.RoundToInt(potTemp)).Append(" C, fire ")
              .Append(potFire ? "ON" : "off").Append('\n');
            sb.Append("In pot: ").Append(Defs.Describe(potEntries)).Append('\n');
            sb.Append("Orders:");
            foreach (var o in orders)
                sb.Append("\n  #").Append(o.id).Append("  ").Append(o.spice == 0 ? "no chili" : o.spice + " chili")
                  .Append(o.state == 1 ? "  eating..." : "  (" + Mathf.CeilToInt(o.left) + "s)");
            if (orders.Count == 0) sb.Append(" none yet");

            string held = "";
            foreach (var it in items.Values)
                if (it.holder == myId) held = Defs.Names[(int)it.type] + (it.chopped ? " (chopped)" : "") + (it.filled ? " (soup)" : "");
            sb.Append("\nHolding: ").Append(held.Length > 0 ? held : "nothing");

            float w = 430f * u;
            float h = sLabel.CalcHeight(new GUIContent(sb.ToString()), w - 12f * u) + 10f * u;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(8f * u, 8f * u, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(14f * u, 12f * u, w - 12f * u, h), sb.ToString(), sLabel);

            // toasts
            float ty = Screen.height * 0.18f;
            for (int i = toasts.Count - 1; i >= 0; i--)
                if (Time.time > toasts[i].Value) toasts.RemoveAt(i);
            foreach (var t in toasts)
            {
                DrawText(new Rect(Screen.width * 0.5f - 300f * u, ty, 600f * u, 26f * u), t.Key, sCenter);
                ty += 26f * u;
            }

            // crosshair + hint
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            GUI.DrawTexture(new Rect(cx - 3f * u, cy - 3f * u, 6f * u, 6f * u), Texture2D.whiteTexture);
            if (!menuOpen)
                DrawText(new Rect(cx - 320f * u, Screen.height - 95f * u, 640f * u, 50f * u), Hint(), sCenter);

            if (!isHost && !worldReady)
                DrawText(new Rect(cx - 200f * u, cy - 60f * u, 400f * u, 30f * u), "Waiting for the host...", sCenter);

            if (menuOpen) return;

            // virtual stick
            if (local != null && local.StickActive)
            {
                float r = 70f * u;
                GUI.color = new Color(1f, 1f, 1f, 0.2f);
                GUI.DrawTexture(new Rect(local.stickStart.x - r, local.stickStart.y - r, r * 2f, r * 2f), circleTex);
                Vector2 d = Vector2.ClampMagnitude(local.stickCur - local.stickStart, r);
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                GUI.DrawTexture(new Rect(local.stickStart.x + d.x - r * 0.4f, local.stickStart.y + d.y - r * 0.4f, r * 0.8f, r * 0.8f), circleTex);
                GUI.color = Color.white;
            }

            if (GUI.Button(GrabR, "GRAB /\nDROP", sBtn)) DoGrab();
            if (GUI.Button(UseR, "USE", sBtn)) DoUse();
            if (GUI.Button(ThrowR, "THROW", sBtn)) DoThrow();
            if (GUI.Button(MenuR, "Menu", sBtn)) menuOpen = true;
        }

        void DrawMenu(float u)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float w = Mathf.Min(Screen.width * 0.94f, 680f * u);
            var area = new Rect((Screen.width - w) / 2f, 10f * u, w, Screen.height - 20f * u);
            GUILayout.BeginArea(area);
            menuScroll = GUILayout.BeginScrollView(menuScroll);

            GUILayout.Label("Tom Yum Kitchen", sBig);
            float bh = 46f * u;

            if (mode == Mode.Title)
            {
                GUILayout.Label("Your name", sLabel);
                playerName = GUILayout.TextField(playerName, 16, sField, GUILayout.Height(bh));
                GUILayout.Label("Port", sLabel);
                portText = GUILayout.TextField(portText, 5, sField, GUILayout.Height(bh));

                if (GUILayout.Button("HOST a game (friends connect to you)", sBtn, GUILayout.Height(bh * 1.2f))) StartHostGame();

                GUILayout.Space(10f * u);
                GUILayout.Label("Host's IP address", sLabel);
                ipText = GUILayout.TextField(ipText, 45, sField, GUILayout.Height(bh));
                if (GUILayout.Button("JOIN that host", sBtn, GUILayout.Height(bh * 1.2f))) JoinGame();
            }
            else if (mode == Mode.Connecting)
            {
                GUILayout.Label("Connecting...", sLabel);
                if (GUILayout.Button("Cancel", sBtn, GUILayout.Height(bh)))
                {
                    if (net != null) net.Stop();
                    net = null;
                    mode = Mode.Title;
                    status = "";
                }
            }
            else
            {
                if (GUILayout.Button("Resume", sBtn, GUILayout.Height(bh * 1.2f))) menuOpen = false;
                if (GUILayout.Button("Leave game", sBtn, GUILayout.Height(bh))) Leave("");
                if (isHost)
                {
                    var sb = new StringBuilder("You are the host. Friends join with one of these IPs, port " + Port() + ".\nConnected players:");
                    foreach (var ps in players.Values)
                        if (ps.id != 0) sb.Append("\n  ").Append(ps.name).Append("  (their IP: ").Append(net.Addr(ps.id)).Append(")");
                    if (players.Count <= 1) sb.Append(" nobody yet");
                    GUILayout.Label(sb.ToString(), sLabel);
                }
            }

            if (status.Length > 0) GUILayout.Label(status, sLabel);

            GUILayout.Space(14f * u);
            GUILayout.Label("YOUR IP ADDRESSES", sBig);
            GUILayout.Label("Local (same Wi-Fi / LAN): " + localIp, sLabel);
            GUILayout.Label("Public (internet): " + (publicIp.Length > 0 ? publicIp : "not looked up"), sLabel);
            if (GUILayout.Button("Look up my public IP (asks api.ipify.org)", sBtn, GUILayout.Height(bh)))
            {
                localIp = GetLocalIp();
                StartCoroutine(FetchPublicIp());
            }
            GUILayout.Label(
                "Privacy: this game is direct peer-to-peer. There is no game server. The host sees the IP of every player " +
                "who joins, and every player sees the host's IP (that is how the connection works). " +
                "Nothing is stored or sent anywhere else. Your public IP is only looked up if you press the button above.\n" +
                "Tip: on the same Wi-Fi use the local IP. Over the internet the host must forward the port on the router, " +
                "or you can both use a VPN like Tailscale / ZeroTier / Radmin and use the VPN IP.",
                sLabel);

            GUILayout.Space(10f * u);
            GUILayout.Label(
                "HOW TO COOK TOM YUM\n" +
                "1. Take ingredients from the bins on the back wall (GRAB). Take a Bowl too.\n" +
                "2. Chop mushroom, lemongrass, galangal, chili at the cutting board (USE, 5 chops).\n" +
                "3. USE on the pot with empty hands to pour water, USE on the stove to light it.\n" +
                "4. THROW or drop ingredients into the pot, or hold them and press USE on the pot. Wait until the soup boils: shrimp and mushroom need about 8 seconds of boiling, but don't burn them.\n" +
                "5. Hold a Bowl and press USE on the pot to ladle. Put the bowl on the green pad (or USE there) to serve.\n" +
                "Recipe: shrimp, mushroom, lemongrass, galangal, kaffir leaf, lime, fish sauce, tomato (bonus) and the number of chopped chilies the order asks for. Soup must be hot.\n" +
                "Desktop keys: WASD, mouse, E/click = grab, F/right click = use, G = throw, Esc = menu.",
                sLabel);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
