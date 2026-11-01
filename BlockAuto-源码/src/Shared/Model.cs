using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BlockAuto
{
    public sealed class PinLink
    {
        public string Port;
        public string From; // "blockId.port" 或 ""

        public PinLink() { Port = ""; From = ""; }
        public PinLink(string port, string from) { Port = port; From = from; }

        public bool HasSource { get { return !string.IsNullOrEmpty(From); } }

        public string SourceBlockId
        {
            get
            {
                int i = From.IndexOf('.');
                return i > 0 ? From.Substring(0, i) : From;
            }
        }

        public string SourcePort
        {
            get
            {
                int i = From.IndexOf('.');
                return i > 0 ? From.Substring(i + 1) : "";
            }
        }
    }

    public sealed class BranchBody
    {
        public string Name;
        public List<Block> Blocks = new List<Block>();
        public BranchBody(string name) { Name = name; }
    }

    public sealed class Block
    {
        public string Id;
        public string Type;
        public string Category;
        public Dictionary<string, object> Params = new Dictionary<string, object>();
        public List<PinLink> Inputs = new List<PinLink>();
        public List<BranchBody> Branches = new List<BranchBody>();
        public string Next;
        public double X;
        public double Y;
        public bool Breakpoint;
        /// <summary>禁用：保留在列表里但运行时跳过</summary>
        public bool Disabled;
        public string Comment = "";

        public bool IsContainer { get { return Branches.Count > 0; } }

        public PinLink Input(string port)
        {
            foreach (PinLink p in Inputs) if (p.Port == port) return p;
            return null;
        }

        public PinLink EnsureInput(string port)
        {
            PinLink p = Input(port);
            if (p == null)
            {
                p = new PinLink(port, "");
                Inputs.Add(p);
            }
            return p;
        }

        public BranchBody Branch(string name)
        {
            foreach (BranchBody b in Branches) if (b.Name == name) return b;
            return null;
        }

        public object Param(string name, object def)
        {
            object v;
            if (Params.TryGetValue(name, out v)) return v;
            return def;
        }
    }

    public sealed class VarDef
    {
        public string Name;
        public string Type = "num";
        public object Init;

        public VarDef() { }
        public VarDef(string name, string type, object init)
        {
            Name = name; Type = type; Init = init;
        }

        public object DefaultValue()
        {
            return ScriptDocument.CoerceDefault(Type, Init);
        }
    }

    public sealed class FunctionDef
    {
        public string Name;
        public List<VarDef> Params = new List<VarDef>();
        public bool HasReturn;
        public List<Block> Body = new List<Block>();
    }

    public sealed class ScriptDocument
    {
        public string Schema = "1.0";
        public string Name = "未命名脚本";
        public string Description = "";
        public List<VarDef> GlobalVars = new List<VarDef>();
        public List<FunctionDef> Functions = new List<FunctionDef>();
        public List<Block> Blocks = new List<Block>();
        public Dictionary<string, object> Settings = new Dictionary<string, object>();
        public string FilePath = "";
        public bool Dirty;

        public string Directory
        {
            get { return string.IsNullOrEmpty(FilePath) ? Environment.CurrentDirectory : Path.GetDirectoryName(FilePath); }
        }

        public static ScriptDocument CreateNew()
        {
            ScriptDocument d = new ScriptDocument();
            d.Settings["max_recursion"] = 100d;
            d.Settings["default_threshold"] = 0.8d;
            d.Blocks.Add(d.NewBlock("log"));
            d.Blocks[0].Params["text"] = "开始运行";
            return d;
        }

        public int NextIdNumber()
        {
            int max = 0;
            foreach (Block b in AllBlocks())
            {
                if (b.Id != null && b.Id.StartsWith("b_"))
                {
                    int n;
                    if (int.TryParse(b.Id.Substring(2), out n) && n > max) max = n;
                }
            }
            return max + 1;
        }

        public string NewId()
        {
            return "b_" + NextIdNumber().ToString("000");
        }

        /// <summary>
        /// 给一批「还没加入文档」的积木分配唯一 id。
        /// NewBlock() 依赖文档里已有的积木编号，批量创建（例如录制）时会出现重复 id，
        /// 运行时校验会判「积木 ID 重复」而拒绝执行，所以批量创建后必须调用一次。
        /// </summary>
        public void AssignNewIds(List<Block> blocks)
        {
            if (blocks == null || blocks.Count == 0) return;
            int seq = NextIdNumber();
            foreach (Block b in blocks)
            {
                if (b == null) continue;
                b.Id = "b_" + (seq++).ToString("000");
                // 容器分支里的子积木也要一起换，避免和别的积木撞号
                foreach (BranchBody br in b.Branches)
                {
                    foreach (Block child in br.Blocks)
                    {
                        child.Id = "b_" + (seq++).ToString("000");
                    }
                }
            }
        }

        /// <summary>根据目录信息创建一个带默认参数与分支的积木。</summary>
        public Block NewBlock(string type)
        {
            BlockInfo info = Catalog.Get(type);
            Block b = new Block();
            b.Type = type;
            b.Id = NewId();
            b.Category = info != null ? info.Category : BlockCategory.Tool;
            if (info != null)
            {
                foreach (ParamSpec p in info.Params)
                {
                    if (p.Name == "branches" && type == "if") continue;
                    b.Params[p.Name] = p.Default;
                }
                foreach (PinLink existing in b.Inputs) { }
                foreach (BranchSpec br in info.Branches)
                    b.Branches.Add(new BranchBody(br.Name));
            }
            if (type == "if")
            {
                List<object> names = new List<object>();
                names.Add("main");
                names.Add("else");
                b.Params["branches"] = names;
                b.Branches.Clear();
                b.Branches.Add(new BranchBody("main"));
                b.Branches.Add(new BranchBody("else"));
            }
            // 补齐端口定义，保证存盘后 inputs 可读
            foreach (PortSpec p in Catalog.InputPorts(b, this))
                b.Inputs.Add(new PinLink(p.Name, ""));
            return b;
        }

        public IEnumerable<Block> AllBlocks()
        {
            foreach (Block b in Blocks) foreach (Block x in Walk(b)) yield return x;
            foreach (FunctionDef f in Functions) foreach (Block b in f.Body) foreach (Block x in Walk(b)) yield return x;
        }

        public static IEnumerable<Block> Walk(Block b)
        {
            yield return b;
            foreach (BranchBody br in b.Branches)
                foreach (Block c in br.Blocks)
                    foreach (Block x in Walk(c)) yield return x;
        }

        public IEnumerable<Block> Roots()
        {
            foreach (Block b in Blocks) yield return b;
            foreach (FunctionDef f in Functions)
                foreach (Block b in f.Body) yield return b;
        }

        public Block FindBlock(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (Block b in AllBlocks()) if (b.Id == id) return b;
            return null;
        }

        public FunctionDef FindFunction(string name)
        {
            foreach (FunctionDef f in Functions) if (f.Name == name) return f;
            return null;
        }

        public VarDef FindGlobal(string name)
        {
            foreach (VarDef v in GlobalVars) if (v.Name == name) return v;
            return null;
        }

        public void EnsureNextLinks()
        {
            foreach (List<Block> list in AllLists()) LinkNext(list);
        }

        private IEnumerable<List<Block>> AllLists()
        {
            yield return Blocks;
            foreach (Block b in AllBlocks())
                foreach (BranchBody br in b.Branches) yield return br.Blocks;
            foreach (FunctionDef f in Functions) yield return f.Body;
        }

        private static void LinkNext(List<Block> list)
        {
            for (int i = 0; i < list.Count; i++)
                list[i].Next = (i + 1 < list.Count) ? list[i + 1].Id : null;
        }

        public List<Block> ParentListOf(Block block)
        {
            if (Blocks.Contains(block)) return Blocks;
            foreach (FunctionDef f in Functions) if (f.Body.Contains(block)) return f.Body;
            foreach (Block b in AllBlocks())
                foreach (BranchBody br in b.Branches)
                    if (br.Blocks.Contains(block)) return br.Blocks;
            return null;
        }

        public Block ParentContainerOf(Block block)
        {
            foreach (Block b in AllBlocks())
                foreach (BranchBody br in b.Branches)
                    if (br.Blocks.Contains(block)) return b;
            return null;
        }

        public string ParentBranchOf(Block block)
        {
            foreach (Block b in AllBlocks())
                foreach (BranchBody br in b.Branches)
                    if (br.Blocks.Contains(block)) return br.Name;
            return null;
        }

        public Block CloneBlock(Block src, List<Block> cloneRootList)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            Block copy = CloneRecursive(src, map);
            RemapLinks(copy, map);
            return copy;
        }

        private Block CloneRecursive(Block src, Dictionary<string, string> map)
        {
            Block b = new Block();
            b.Id = NewId();
            b.Type = src.Type;
            b.Category = src.Category;
            foreach (KeyValuePair<string, object> kv in src.Params) b.Params[kv.Key] = CloneValue(kv.Value);
            b.Inputs = new List<PinLink>();
            foreach (PinLink p in src.Inputs) b.Inputs.Add(new PinLink(p.Port, p.From));
            b.Branches = new List<BranchBody>();
            b.X = src.X; b.Y = src.Y;
            b.Breakpoint = src.Breakpoint;
            b.Comment = src.Comment;
            b.Next = src.Next;
            map[src.Id] = b.Id;
            foreach (BranchBody br in src.Branches)
            {
                BranchBody nb = new BranchBody(br.Name);
                foreach (Block c in br.Blocks) nb.Blocks.Add(CloneRecursive(c, map));
                b.Branches.Add(nb);
            }
            return b;
        }

        private void RemapLinks(Block b, Dictionary<string, string> map)
        {
            foreach (PinLink p in b.Inputs)
            {
                if (!string.IsNullOrEmpty(p.From))
                {
                    string sid = p.SourceBlockId;
                    string newId;
                    if (map.TryGetValue(sid, out newId))
                        p.From = newId + "." + p.SourcePort;
                }
            }
            if (b.Next != null)
            {
                string nid;
                if (map.TryGetValue(b.Next, out nid)) b.Next = nid;
            }
            foreach (BranchBody br in b.Branches)
                foreach (Block c in br.Blocks) RemapLinks(c, map);
        }

        private static object CloneValue(object v)
        {
            if (v == null) return null;
            List<object> l = v as List<object>;
            if (l != null)
            {
                List<object> r = new List<object>();
                foreach (object x in l) r.Add(CloneValue(x));
                return r;
            }
            Dictionary<string, object> d = v as Dictionary<string, object>;
            if (d != null)
            {
                Dictionary<string, object> r = new Dictionary<string, object>();
                foreach (KeyValuePair<string, object> kv in d) r[kv.Key] = CloneValue(kv.Value);
                return r;
            }
            return v;
        }

        public string ResolveTemplatePath(string relative)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            if (Path.IsPathRooted(relative)) return relative; // 运行时仍允许，但校验期会警告
            return Path.GetFullPath(Path.Combine(Directory, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        public static object CoerceDefault(string type, object init)
        {
            if (type == "num") return Json.ToNumber(init, 0);
            if (type == "bool") return Json.ToBool(init, false);
            if (type == "str") return init == null ? "" : Convert.ToString(init, CultureInfo.InvariantCulture);
            if (type == "array")
            {
                List<object> l = init as List<object>;
                return l ?? new List<object>();
            }
            if (type == "dict")
            {
                Dictionary<string, object> d = init as Dictionary<string, object>;
                return d ?? new Dictionary<string, object>();
            }
            return init;
        }

        // ---------------------------- 序列化 ----------------------------

        public string ToJson()
        {
            EnsureNextLinks();
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["schema"] = Schema;
            root["name"] = Name;
            root["description"] = Description;
            List<object> gv = new List<object>();
            foreach (VarDef v in GlobalVars)
            {
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["name"] = v.Name;
                o["type"] = v.Type;
                o["init"] = v.Init;
                gv.Add(o);
            }
            root["global_vars"] = gv;
            List<object> fs = new List<object>();
            foreach (FunctionDef f in Functions)
            {
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["name"] = f.Name;
                List<object> ps = new List<object>();
                foreach (VarDef p in f.Params)
                {
                    Dictionary<string, object> po = new Dictionary<string, object>();
                    po["name"] = p.Name;
                    po["type"] = p.Type;
                    ps.Add(po);
                }
                o["params"] = ps;
                o["has_return"] = f.HasReturn;
                List<object> body = new List<object>();
                foreach (Block b in f.Body) body.Add(BlockToJson(b));
                o["body"] = body;
                fs.Add(o);
            }
            root["functions"] = fs;
            List<object> blocks = new List<object>();
            foreach (Block b in Blocks) blocks.Add(BlockToJson(b));
            root["blocks"] = blocks;
            root["settings"] = Settings;
            return Json.Stringify(root, true);
        }

        private object BlockToJson(Block b)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["id"] = b.Id;
            o["type"] = b.Type;
            o["category"] = b.Category;
            Dictionary<string, object> ps = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> kv in b.Params)
            {
                if (b.Type == "if" && kv.Key == "branches") continue;
                ps[kv.Key] = kv.Value;
            }
            o["params"] = ps;
            List<object> ins = new List<object>();
            foreach (PinLink p in b.Inputs)
            {
                Dictionary<string, object> po = new Dictionary<string, object>();
                po["port"] = p.Port;
                po["from"] = p.From;
                ins.Add(po);
            }
            o["inputs"] = ins;
            if (b.Branches.Count > 0)
            {
                if (b.Branches.Count == 1)
                {
                    List<object> children = new List<object>();
                    foreach (Block c in b.Branches[0].Blocks) children.Add(BlockToJson(c));
                    o["children"] = children;
                }
                else
                {
                    List<object> branches = new List<object>();
                    foreach (BranchBody br in b.Branches)
                    {
                        List<object> children = new List<object>();
                        foreach (Block c in br.Blocks) children.Add(BlockToJson(c));
                        branches.Add(children);
                    }
                    o["children"] = branches;
                    List<object> names = new List<object>();
                    foreach (BranchBody br in b.Branches) names.Add(br.Name);
                    if (b.Type == "if") ps["branches"] = names;
                }
            }
            o["next"] = b.Next;
            o["x"] = b.X;
            o["y"] = b.Y;
            o["breakpoint"] = b.Breakpoint;
            o["disabled"] = b.Disabled;
            o["comment"] = b.Comment;
            return o;
        }

        public static ScriptDocument FromJson(string text, string filePath)
        {
            Dictionary<string, object> root = Json.AsObject(Json.Parse(text));
            ScriptDocument d = new ScriptDocument();
            d.FilePath = filePath ?? "";
            d.Schema = Json.GetString(root, "schema", "1.0");
            d.Name = Json.GetString(root, "name", "未命名脚本");
            d.Description = Json.GetString(root, "description", "");

            List<object> gv = Json.ArrayAt(root, "global_vars");
            if (gv != null)
            {
                foreach (object it in gv)
                {
                    Dictionary<string, object> o = Json.AsObject(it);
                    VarDef v = new VarDef();
                    v.Name = Json.GetString(o, "name", "");
                    v.Type = Json.GetString(o, "type", "num");
                    o.TryGetValue("init", out v.Init);
                    if (v.Name.Length > 0) d.GlobalVars.Add(v);
                }
            }

            List<object> fs = Json.ArrayAt(root, "functions");
            if (fs != null)
            {
                foreach (object it in fs)
                {
                    Dictionary<string, object> o = Json.AsObject(it);
                    FunctionDef f = new FunctionDef();
                    f.Name = Json.GetString(o, "name", "");
                    f.HasReturn = Json.GetBool(o, "has_return", false);
                    List<object> ps = Json.ArrayAt(o, "params");
                    if (ps != null)
                    {
                        foreach (object pit in ps)
                        {
                            Dictionary<string, object> po = Json.AsObject(pit);
                            f.Params.Add(new VarDef(Json.GetString(po, "name", "arg"), Json.GetString(po, "type", "any"), null));
                        }
                    }
                    List<object> body = Json.ArrayAt(o, "body");
                    if (body != null)
                        foreach (object bit in body) f.Body.Add(BlockFromJson(Json.AsObject(bit)));
                    if (f.Name.Length > 0) d.Functions.Add(f);
                }
            }

            List<object> blocks = Json.ArrayAt(root, "blocks");
            if (blocks != null)
                foreach (object bit in blocks) d.Blocks.Add(BlockFromJson(Json.AsObject(bit)));

            Dictionary<string, object> st = Json.ObjectAt(root, "settings");
            if (st != null) d.Settings = st;
            if (!d.Settings.ContainsKey("max_recursion")) d.Settings["max_recursion"] = 100d;
            if (!d.Settings.ContainsKey("default_threshold")) d.Settings["default_threshold"] = 0.8d;

            d.NormalizeBranches();
            d.EnsureNextLinks();
            d.Dirty = false;
            return d;
        }

        public static Block BlockFromJson(Dictionary<string, object> o)
        {
            Block b = new Block();
            b.Id = Json.GetString(o, "id", "");
            b.Type = Json.GetString(o, "type", "comment");
            b.Category = Json.GetString(o, "category", Catalog.CategoryOf(b.Type));
            Dictionary<string, object> ps = Json.ObjectAt(o, "params");
            if (ps != null) b.Params = ps;
            List<object> ins = Json.ArrayAt(o, "inputs");
            if (ins != null)
            {
                foreach (object it in ins)
                {
                    Dictionary<string, object> po = Json.AsObject(it);
                    b.Inputs.Add(new PinLink(Json.GetString(po, "port", ""), Json.GetString(po, "from", "")));
                }
            }
            List<object> children = Json.ArrayAt(o, "children");
            if (children != null)
            {
                if (children.Count > 0 && children[0] is List<object>)
                {
                    foreach (object cit in children)
                    {
                        List<object> arr = cit as List<object>;
                        BranchBody br = new BranchBody("branch" + b.Branches.Count);
                        if (arr != null) foreach (object bit in arr) br.Blocks.Add(BlockFromJson(Json.AsObject(bit)));
                        b.Branches.Add(br);
                    }
                }
                else
                {
                    BranchBody br = new BranchBody(b.Type == "while" || b.Type == "for_count" || b.Type == "for_each" ? "body" : "main");
                    foreach (object bit in children) br.Blocks.Add(BlockFromJson(Json.AsObject(bit)));
                    b.Branches.Add(br);
                }
            }
            b.Next = null;
            object nx;
            if (o.TryGetValue("next", out nx) && nx != null) b.Next = Convert.ToString(nx);
            b.X = Json.GetNumber(o, "x", 0);
            b.Y = Json.GetNumber(o, "y", 0);
            b.Breakpoint = Json.GetBool(o, "breakpoint", false);
            b.Disabled = Json.GetBool(o, "disabled", false);
            b.Comment = Json.GetString(o, "comment", "");
            return b;
        }

        /// <summary>补齐分支数量与名称、缺失的输入端口。</summary>
        public void NormalizeBranches()
        {
            foreach (Block b in AllBlocks())
            {
                List<BranchSpec> specs = Catalog.Branches(b);
                if (specs.Count == 0)
                {
                    b.Branches.Clear();
                }
                else
                {
                    List<BranchBody> old = new List<BranchBody>(b.Branches);
                    b.Branches.Clear();
                    for (int i = 0; i < specs.Count; i++)
                    {
                        BranchBody body = i < old.Count ? old[i] : new BranchBody(specs[i].Name);
                        body.Name = specs[i].Name;
                        b.Branches.Add(body);
                    }
                }
                foreach (PortSpec p in Catalog.InputPorts(b, this))
                    if (b.Input(p.Name) == null) b.Inputs.Add(new PinLink(p.Name, ""));
            }
        }

        public void Save(string path)
        {
            EnsureNextLinks();
            File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
            FilePath = path;
            Dirty = false;
        }

        public static ScriptDocument Load(string path)
        {
            ScriptDocument d = FromJson(File.ReadAllText(path, Encoding.UTF8), path);
            d.MigrateLegacy();
            d.ValidateLoose();
            return d;
        }

        /// <summary>
        /// v2.5：识图能力并入「鼠标点击」。打开旧脚本时，把「识图积木 + 紧接着绑定它的鼠标点击」
        /// 合并成一步自动识图点击，用户不再需要单独放识图积木。返回合并的步数。
        /// </summary>
        public int MigrateLegacy()
        {
            int merged = 0;
            foreach (List<Block> list in new List<List<Block>>(AllLists())) merged += MergeVisionClicks(list);
            EnsureNextLinks();
            return merged;
        }

        private int MergeVisionClicks(List<Block> list)
        {
            if (list == null) return 0;
            int merged = 0;
            for (int i = 0; i < list.Count - 1; i++)
            {
                Block fi = list[i];
                Block click = list[i + 1];
                if (fi.Type != "find_image" || click.Type != "mouse_click") continue;
                if (Json.GetString(click.Params, "coord_mode", "recorded") != "bound") continue;
                PinLink lx = click.Input("x");
                PinLink ly = click.Input("y");
                bool fromThis = (lx != null && lx.SourceBlockId == fi.Id) || (ly != null && ly.SourceBlockId == fi.Id);
                if (!fromThis) continue;

                click.Params["coord_mode"] = "image";
                click.Params["template"] = Json.GetString(fi.Params, "template", Json.GetString(click.Params, "template", ""));
                click.Params["region"] = Json.GetString(fi.Params, "region", Json.GetString(click.Params, "region", "active"));
                click.Params["threshold"] = Json.GetNumber(fi.Params, "threshold", Json.GetNumber(click.Params, "threshold", 0.8d));
                if (lx != null) lx.From = "";
                if (ly != null) ly.From = "";
                list.RemoveAt(i);
                merged++;
                i--;
            }
            return merged;
        }

        // ---------------------------- 校验 ----------------------------

        public List<string> Validate()
        {
            List<string> errors = new List<string>();
            HashSet<string> ids = new HashSet<string>();
            foreach (Block b in AllBlocks())
            {
                if (string.IsNullOrEmpty(b.Id)) { errors.Add("存在缺少 id 的积木"); continue; }
                if (!ids.Add(b.Id)) errors.Add("积木 ID 重复：" + b.Id);
                if (Catalog.Get(b.Type) == null) errors.Add("未知积木类型：" + b.Type + "（" + b.Id + "）");
            }
            foreach (Block b in AllBlocks())
            {
                foreach (PinLink p in b.Inputs)
                {
                    if (string.IsNullOrEmpty(p.From)) continue;
                    int dot = p.From.IndexOf('.');
                    if (dot <= 0) { errors.Add("连线格式错误：" + b.Id + "." + p.Port + " -> " + p.From); continue; }
                    string sid = p.From.Substring(0, dot);
                    if (FindBlock(sid) == null) errors.Add("连线引用了不存在的积木：" + sid + "（" + b.Id + "." + p.Port + "）");
                }
                if (b.Next != null && FindBlock(b.Next) == null)
                    errors.Add("next 指向不存在的积木：" + b.Next + "（" + b.Id + "）");
                if (b.Type == "call_func")
                {
                    string fn = Json.GetString(b.Params, "func", "");
                    FunctionDef f = FindFunction(fn);
                    if (f == null) errors.Add("调用函数不存在：" + fn + "（" + b.Id + "）");
                }
                if (b.Type == "find_image")
                {
                    string tpl = Json.GetString(b.Params, "template", "");
                    if (string.IsNullOrEmpty(tpl)) errors.Add("识图积木未选择模板：" + b.Id);
                    else if (Path.IsPathRooted(tpl)) errors.Add("模板必须使用相对路径：" + b.Id + " -> " + tpl);
                }
                if (b.Type == "mouse_click" && Json.GetString(b.Params, "coord_mode", "recorded") == "image")
                {
                    string tpl = Json.GetString(b.Params, "template", "");
                    if (string.IsNullOrEmpty(tpl)) errors.Add("鼠标点击选了「自动识图」但还没截图取图：" + b.Id);
                    else if (Path.IsPathRooted(tpl)) errors.Add("模板必须使用相对路径：" + b.Id + " -> " + tpl);
                }
            }
            foreach (FunctionDef f in Functions)
            {
                if (FindFunction(f.Name) == null) errors.Add("函数名缺失");
                HashSet<string> pnames = new HashSet<string>();
                foreach (VarDef p in f.Params)
                    if (!pnames.Add(p.Name)) errors.Add("函数 " + f.Name + " 参数重名：" + p.Name);
            }
            return errors;
        }

        /// <summary>宽松校验：加载时只修复明显问题，不阻断。</summary>
        public void ValidateLoose()
        {
            foreach (Block b in AllBlocks())
            {
                if (Catalog.Get(b.Type) == null)
                {
                    // 未知类型保留为 tool，方便排查，但引擎会报结构错误
                    b.Category = BlockCategory.Tool;
                }
            }
        }

        public string TitleForDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(FilePath)) return Name + " *";
                return Path.GetFileName(FilePath) + (Dirty ? " *" : "");
            }
        }
    }
}
