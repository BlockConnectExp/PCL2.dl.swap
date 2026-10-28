using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace PclDlSwap.Patcher
{
    /// <summary>
    /// Rewrites an official PCL executable on disk so that:
    ///   1. the download page works again (2.99.0 replaced the loader with a throwing stub),
    ///   2. silent OTA becomes an explicit, cancellable prompt,
    ///   3. the auto-update permission gate is hard-denied.
    ///
    /// Every rewrite only uses types the target already references
    /// (<c>PCL.ModMain.MyMsgBox</c>, <c>PCL.ModDownload.DlClientListLoader</c>, …), so the patched
    /// module gains no new AssemblyRef. That constraint is deliberate: keeping the dependency table
    /// identical is what lets the rewrite survive both the CLR loader and future official updates,
    /// and it is the same constraint that made every in-memory/cross-assembly approach unusable.
    ///
    /// The payload DLL (sandbox scanner + watchdog + logging) is used at patch time for scanning,
    /// and the watchdog is installed by the patched host through a small bootstrap class.
    /// </summary>
    public sealed class IlPatcher
    {
        private readonly Action<string> _log;

        public IlPatcher(string payloadAssemblyPath, Action<string> log = null, bool runtimeBootstrap = true)
        {
            _log = log ?? (_ => { });
            PayloadAssemblyPath = payloadAssemblyPath;
            RuntimeBootstrap = runtimeBootstrap;
        }

        public string PayloadAssemblyPath { get; }

        /// <summary>
        /// When true (default) the patched module also gains the runtime bootstrap call, so the
        /// disk-patch shape boots the same SwapHost platform as the injected shape.
        /// </summary>
        public bool RuntimeBootstrap { get; }

        public sealed class PatchResult
        {
            public string OutputPath { get; set; }
            public string Sha256 { get; set; }
            public int Rewrites { get; set; }
            public List<string> Notes { get; } = new List<string>();
        }

        public PatchResult Patch(string sourcePath, string outputPath)
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("找不到目标程序", sourcePath);

            var result = new PatchResult { OutputPath = outputPath };
            using (var module = ModuleDefMD.Load(sourcePath))
            {
                RewriteDownloadGate(module, result);
                RewriteDownloadSidebar(module, result);
                RewriteSilentOta(module, result);
                RewriteUpdatePermission(module, result);
                if (RuntimeBootstrap) RewriteRuntimeBootstrap(module, result);

                var options = new dnlib.DotNet.Writer.ModuleWriterOptions(module);
                options.MetadataOptions.Flags |= dnlib.DotNet.Writer.MetadataFlags.KeepOldMaxStack;
                module.Write(outputPath, options);
            }

            result.Sha256 = Sha256(outputPath);
            _log($"补丁输出：{outputPath}（{result.Rewrites} 处重写，sha256={result.Sha256.Substring(0, 16)}…）");
            return result;
        }

        // ------------------------------------------------------------------ lookups

        private static TypeDef FindType(ModuleDefMD module, string fullName) =>
            module.GetTypes().FirstOrDefault(t => t.FullName == fullName);

        private static MethodDef FindMethod(ModuleDefMD module, string typeName, string methodName, int parameterCount = -1)
        {
            var type = FindType(module, typeName);
            if (type == null) return null;
            return type.Methods.FirstOrDefault(m =>
                m.Name == methodName && (parameterCount < 0 || m.Parameters.Count == parameterCount));
        }

        private static IMethod HostMethod(ModuleDefMD module, TypeDef declaring, string name, TypeSig returnType, params TypeSig[] parameters)
        {
            var existing = declaring.Methods.FirstOrDefault(m =>
                m.Name == name && m.Parameters.Count == parameters.Length && m.IsStatic);
            if (existing != null) return existing;

            var signature = MethodSig.CreateStatic(returnType, parameters);
            return new MemberRefUser(module, name, signature, declaring);
        }

        // ------------------------------------------------------------------ 5. runtime bootstrap

        /// <summary>
        /// Gives the disk-patch shape the same runtime platform as the injected one: the patched
        /// module gains exactly one new AssemblyRef (<c>PclDlSwap.Payload</c>) plus a call to
        /// <c>HookEntryPoints.Bootstrap("disk-patch")</c> at the end of PCL's <c>ModMain..cctor</c>.
        /// The type finishes its own field initialisation first, then the watchdog and SwapHost
        /// (module platform, pages, hooks, event bus) start before Main runs. The runtime files ship
        /// next to the patched executable.
        /// </summary>
        private void RewriteRuntimeBootstrap(ModuleDefMD module, PatchResult result)
        {
            if (string.IsNullOrEmpty(PayloadAssemblyPath) || !File.Exists(PayloadAssemblyPath))
            {
                result.Notes.Add("缺少 payload 程序集，运行时 bootstrap 跳过。");
                _log(result.Notes[result.Notes.Count - 1]);
                return;
            }

            var cctor = FindMethod(module, "PCL.ModMain", ".cctor");
            if (cctor?.Body == null)
            {
                result.Notes.Add("未找到 PCL.ModMain::.cctor，运行时 bootstrap 跳过（该构建可能改了启动布局）。");
                _log(result.Notes[result.Notes.Count - 1]);
                return;
            }

            var already = cctor.Body.Instructions.Any(instruction =>
                (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                instruction.Operand is IMethod method &&
                method.DeclaringType?.FullName == "PclDlSwap.Payload.Host.HookEntryPoints" &&
                method.Name == "Bootstrap");
            if (already)
            {
                result.Notes.Add("运行时 bootstrap 已存在，跳过。");
                return;
            }

            string payloadName;
            Version payloadVersion;
            using (var payloadModule = ModuleDefMD.Load(PayloadAssemblyPath))
            {
                payloadName = payloadModule.Assembly?.Name ?? "PclDlSwap.Payload";
                payloadVersion = payloadModule.Assembly?.Version ?? new Version(0, 0, 0, 0);
            }

            var reference = module.GetAssemblyRefs().FirstOrDefault(candidate =>
                string.Equals(candidate.Name, payloadName, StringComparison.OrdinalIgnoreCase));
            if (reference == null)
            {
                // dnlib registers the AssemblyRef implicitly through the TypeRef/MemberRef scopes,
                // so it is enough to build the reference and use it below.
                reference = new AssemblyRefUser(new AssemblyNameInfo(payloadName)
                {
                    Version = payloadVersion,
                    Culture = UTF8String.Empty
                });
            }

            var entryType = new TypeRefUser(module, "PclDlSwap.Payload.Host", "HookEntryPoints", reference);
            var bootstrap = new MemberRefUser(module, "Bootstrap",
                MethodSig.CreateStatic(module.CorLibTypes.Int32, module.CorLibTypes.String), entryType);

            var instructions = cctor.Body.Instructions;
            var lastReturn = -1;
            for (var index = 0; index < instructions.Count; index++)
                if (instructions[index].OpCode == OpCodes.Ret) lastReturn = index;
            if (lastReturn < 0)
            {
                result.Notes.Add("ModMain::.cctor 中没有 ret，运行时 bootstrap 跳过。");
                _log(result.Notes[result.Notes.Count - 1]);
                return;
            }

            instructions.Insert(lastReturn, Instruction.Create(OpCodes.Ldstr, "disk-patch"));
            instructions.Insert(lastReturn + 1, Instruction.Create(OpCodes.Call, bootstrap));
            instructions.Insert(lastReturn + 2, Instruction.Create(OpCodes.Pop));

            result.Rewrites++;
            result.Notes.Add("运行时 bootstrap：ModMain..cctor 末尾插入 HookEntryPoints.Bootstrap(\"disk-patch\")" +
                             $"（新增 AssemblyRef {payloadName} {payloadVersion}）");
            _log(result.Notes[result.Notes.Count - 1]);
        }

        // ------------------------------------------------------------------ 1. download gate

        /// <summary>
        /// 2.99.0 passes a throwing loader as the fifth argument of PageLoaderInit. Replacing that
        /// argument with the real loader (which is still present) restores the download page and the
        /// "change game version" flow in one shot.
        /// </summary>
        private void RewriteDownloadGate(ModuleDefMD module, PatchResult result)
        {
            var loaderField = FindType(module, "PCL.ModDownload")?.Fields.FirstOrDefault(f => f.Name == "DlClientListLoader");
            if (loaderField == null)
            {
                result.Notes.Add("未找到 ModDownload.DlClientListLoader：该构建已删除下载模块，需要携带自持模块。");
                _log(result.Notes[result.Notes.Count - 1]);
                return;
            }

            var loaderInit = FindMethod(module, "PCL.PageDownloadInstall", "LoaderInit");
            if (loaderInit?.Body == null)
            {
                result.Notes.Add("未找到 PageDownloadInstall.LoaderInit，下载门重写跳过。");
                _log(result.Notes[result.Notes.Count - 1]);
                return;
            }

            var instructions = loaderInit.Body.Instructions;
            var call = instructions.FirstOrDefault(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
                i.Operand is IMethod method && method.Name == "PageLoaderInit");
            if (call == null)
            {
                result.Notes.Add("LoaderInit 中未找到 PageLoaderInit 调用，下载门重写跳过。");
                _log(result.Notes[result.Notes.Count - 1]);
                return;
            }

            int index = instructions.IndexOf(call);

            // Observed shape in 2.99.0 (verified by dumping the method's IL):
            //   ldnull; ldstr "关闭下载"; ldsfld closure; brfalse; …; newobj LoaderTask`2 ; <finishAction>
            // The gated loader is the argument between the back-action `ldnull` and the finish
            // delegate. Locate it by structure rather than by offset so the patch survives rebuilds.
            int loaderStart = -1;
            int loaderEnd = -1;
            for (int i = index - 1; i >= 0 && i > index - 80; i--)
            {
                if (instructions[i].OpCode != OpCodes.Newobj) continue;
                if (!(instructions[i].Operand is IMethod ctor) || !ctor.Name.Equals(".ctor")) continue;
                if (!ctor.DeclaringType.FullName.StartsWith("PCL.ModLoader/LoaderTask", StringComparison.Ordinal)) continue;

                loaderEnd = i + 1;
                // The construction starts at the ldstr that names the stub loader.
                for (int k = i; k >= 0 && k > index - 80; k--)
                {
                    if (instructions[k].OpCode == OpCodes.Ldstr)
                    {
                        loaderStart = k;
                        break;
                    }
                }
                break;
            }

            if (loaderStart < 0 || loaderEnd <= loaderStart)
            {
                result.Notes.Add("未能定位 PageLoaderInit 的加载器参数，下载门重写跳过。");
                _log(result.Notes[result.Notes.Count - 1]);
                return;
            }

            for (int k = loaderEnd - 1; k >= loaderStart; k--) instructions.RemoveAt(k);
            instructions.Insert(loaderStart, Instruction.Create(OpCodes.Ldsfld, loaderField));

            loaderInit.Body.KeepOldMaxStack = true;
            result.Rewrites++;
            result.Notes.Add("PageDownloadInstall.LoaderInit -> 恢复 ModDownload.DlClientListLoader");
            _log(result.Notes[result.Notes.Count - 1]);
        }

        // ------------------------------------------------------------------ 1b. download sidebar

        /// <summary>
        /// 2.99.0.1 hides the "原版游戏" (vanilla) entry at runtime: its PageDownloadLeft constructor
        /// collapses <c>ItemInstall</c> right after InitializeComponent, switches the default sub-page
        /// to DownloadMod and checks the Mod entry. Those default-page statements are removed so the
        /// sidebar matches 2.99.0.0 again; the loader rewrite above then makes the entry functional.
        ///
        /// <c>LabResource.Visibility = Collapsed</c> (also new in 2.99.0.1) is left untouched: it is an
        /// official element of the new layout, not part of the download gate.
        /// </summary>
        private void RewriteDownloadSidebar(ModuleDefMD module, PatchResult result)
        {
            var type = FindType(module, "PCL.PageDownloadLeft");
            // Note: dnlib reports the hidden 'this' slot among MethodDef.Parameters, so the instance
            // constructor must not be filtered by parameter count (it would show as 1, not 0).
            var ctor = type?.Methods.FirstOrDefault(m => m.Name == ".ctor");
            if (ctor?.Body == null)
            {
                result.Notes.Add("未找到 PageDownloadLeft 构造函数，下载侧栏重写跳过。");
                _log(result.Notes[result.Notes.Count - 1]);
                return;
            }

            var instructions = ctor.Body.Instructions;
            int initIndex = -1;
            for (int i = 0; i < instructions.Count; i++)
            {
                if ((instructions[i].OpCode == OpCodes.Call || instructions[i].OpCode == OpCodes.Callvirt) &&
                    instructions[i].Operand is IMethod method && method.Name == "InitializeComponent")
                {
                    initIndex = i;
                    break;
                }
            }

            if (initIndex < 0)
            {
                result.Notes.Add("PageDownloadLeft 构造函数中未找到 InitializeComponent 调用，下载侧栏重写跳过。");
                _log(result.Notes[result.Notes.Count - 1]);
                return;
            }

            var remove = new SortedSet<int>();

            for (int i = initIndex + 1; i + 3 < instructions.Count; i++)
            {
                // ldarg.0; call get_ItemInstall; ldc.i4.2 (Collapsed); callvirt set_Visibility
                if (instructions[i].OpCode == OpCodes.Ldarg_0 &&
                    IsCallNamed(instructions[i + 1], "get_ItemInstall") &&
                    IsLoadInt32(instructions[i + 2], 2) &&
                    IsCallNamed(instructions[i + 3], "set_Visibility"))
                {
                    for (int k = 0; k < 4; k++) remove.Add(i + k);
                    i += 3;
                    continue;
                }

                // ldarg.0; call get_ItemMod; ldc.i4.1 (true); callvirt set_Checked
                if (instructions[i].OpCode == OpCodes.Ldarg_0 &&
                    IsCallNamed(instructions[i + 1], "get_ItemMod") &&
                    IsLoadInt32(instructions[i + 2], 1) &&
                    IsCallNamed(instructions[i + 3], "set_Checked"))
                {
                    for (int k = 0; k < 4; k++) remove.Add(i + k);
                    i += 3;
                    continue;
                }

                // ldarg.0; ldc.i4 <sub-page>; stfld PageID   (only after InitializeComponent: the
                // 2.99.0.1 default-page override; the 2.99.0.0 assignment happens before the call)
                if (instructions[i].OpCode == OpCodes.Ldarg_0 &&
                    TryGetLoadedInt32(instructions[i + 1], out _) &&
                    instructions[i + 2].OpCode == OpCodes.Stfld &&
                    instructions[i + 2].Operand is IField field && field.Name == "PageID")
                {
                    for (int k = 0; k < 3; k++) remove.Add(i + k);
                    i += 2;
                }
            }

            if (remove.Count == 0)
            {
                result.Notes.Add("PageDownloadLeft 构造函数与 2.99.0.0 一致，无需移除侧栏隐藏语句。");
                _log(result.Notes[result.Notes.Count - 1]);
                return;
            }

            var removed = new HashSet<Instruction>();
            foreach (var index in remove) removed.Add(instructions[index]);
            foreach (var instruction in instructions)
            {
                if (instruction.Operand is Instruction target && removed.Contains(target))
                {
                    result.Notes.Add("待移除的侧栏语句被分支引用，下载侧栏重写跳过。");
                    _log(result.Notes[result.Notes.Count - 1]);
                    return;
                }
            }

            var ordered = remove.ToList();
            for (int i = ordered.Count - 1; i >= 0; i--) instructions.RemoveAt(ordered[i]);

            ctor.Body.KeepOldMaxStack = true;
            result.Rewrites++;
            result.Notes.Add($"PageDownloadLeft..ctor -> 恢复“原版游戏”侧栏项（移除 {ordered.Count} 条隐藏/改默认语句）");
            _log(result.Notes[result.Notes.Count - 1]);
        }

        private static bool IsCallNamed(Instruction instruction, string name) =>
            (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
            instruction.Operand is IMethod method && method.Name == name;

        private static bool IsLoadInt32(Instruction instruction, int value) =>
            TryGetLoadedInt32(instruction, out var observed) && observed == value;

        private static bool TryGetLoadedInt32(Instruction instruction, out int observed)
        {
            observed = 0;
            switch (instruction.OpCode.Code)
            {
                case Code.Ldc_I4_M1: observed = -1; break;
                case Code.Ldc_I4_0: observed = 0; break;
                case Code.Ldc_I4_1: observed = 1; break;
                case Code.Ldc_I4_2: observed = 2; break;
                case Code.Ldc_I4_3: observed = 3; break;
                case Code.Ldc_I4_4: observed = 4; break;
                case Code.Ldc_I4_5: observed = 5; break;
                case Code.Ldc_I4_6: observed = 6; break;
                case Code.Ldc_I4_7: observed = 7; break;
                case Code.Ldc_I4_8: observed = 8; break;
                case Code.Ldc_I4_S: observed = instruction.Operand is sbyte sb ? sb : 0; break;
                case Code.Ldc_I4: observed = instruction.Operand is int i ? i : 0; break;
                default: return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ 2. silent OTA

        /// <summary>
        /// Prefixes the OTA entry point with an explicit confirmation. Because the patched body owns
        /// the whole method, the original IL is preserved after the prompt and the flags never need
        /// to be synthesised: silent requests either stop here or continue as an explicit update.
        /// </summary>
        private void RewriteSilentOta(ModuleDefMD module, PatchResult result)
        {
            var target = FindMethod(module, "PCL.ModSecret", "UpdateStart", 2);
            if (target?.Body == null)
            {
                result.Notes.Add("未找到 ModSecret.UpdateStart(bool, IEnumerable<string>)，静默 OTA 重写跳过。");
                _log(result.Notes[result.Notes.Count - 1]);
                return;
            }

            var modMain = FindType(module, "PCL.ModMain");
            if (modMain == null)
            {
                result.Notes.Add("未找到 PCL.ModMain，无法植入提示对话框；静默 OTA 重写跳过。");
                _log(result.Notes[result.Notes.Count - 1]);
                return;
            }

            var core = module.CorLibTypes;
            var actionSig = new ClassSig(new TypeRefUser(module, "System", "Action", core.AssemblyRef));
            var myMsgBox = HostMethod(module, modMain, "MyMsgBox", core.Int32,
                core.String, core.String, core.String, core.String, core.String,
                core.Boolean, core.Boolean, core.Boolean,
                actionSig, actionSig, actionSig);

            var body = target.Body;
            var original = body.Instructions.ToList();
            var continueUpdate = Instruction.Create(OpCodes.Nop);       // jump target when the user agrees
            var methodReturn = Instruction.Create(OpCodes.Ret);
            var list = new List<Instruction>
            {
                // if (!silent) goto continueUpdate;
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Brfalse, continueUpdate),

                // if (MyMsgBox(...) != 1) return;   (1 = first button = continue)
                Instruction.Create(OpCodes.Ldstr,
                "PCL 正在尝试静默更新（OTA），会在后台替换启动器本体。\r\n\r\n" +
                "本版本（PCL2 DL-Swap）已把静默更新改为显式确认：\r\n" +
                "  · 选择“继续更新”后才会下载，下载内容会先落入沙箱并经过多引擎查毒；\r\n" +
                "  · 未通过检测的更新不会替换启动器。\r\n\r\n" +
                "是否继续本次更新？"),
                Instruction.Create(OpCodes.Ldstr, "PCL2 DL-Swap · 静默更新已拦截"),
                Instruction.Create(OpCodes.Ldstr, "继续更新"),
                Instruction.Create(OpCodes.Ldstr, "取消"),
                Instruction.Create(OpCodes.Ldstr, string.Empty),
                Instruction.Create(OpCodes.Ldc_I4_1),                   // IsWarn
                Instruction.Create(OpCodes.Ldc_I4_1),                   // HighLight
                Instruction.Create(OpCodes.Ldc_I4_1),                   // ForceWait
                Instruction.Create(OpCodes.Ldnull),                     // Button1Action
                Instruction.Create(OpCodes.Ldnull),                     // Button2Action
                Instruction.Create(OpCodes.Ldnull),                     // Button3Action
                Instruction.Create(OpCodes.Call, myMsgBox),
                Instruction.Create(OpCodes.Ldc_I4_1),
                Instruction.Create(OpCodes.Bne_Un, methodReturn),
                methodReturn,
                continueUpdate,
            };

            body.Instructions.Clear();
            foreach (var instruction in list) body.Instructions.Add(instruction);
            foreach (var instruction in original) body.Instructions.Add(instruction);
            body.KeepOldMaxStack = true;

            result.Rewrites++;
            result.Notes.Add("ModSecret.UpdateStart -> 静默 OTA 改为显式确认（可取消）");
            _log(result.Notes[result.Notes.Count - 1]);
        }

        // ------------------------------------------------------------------ 3. permission gate

        private void RewriteUpdatePermission(ModuleDefMD module, PatchResult result)
        {
            var type = FindType(module, "PCL.ModSecret");
            var target = type?.Properties.FirstOrDefault(p => p.Name == "HasUpdatePermission")?.GetMethod
                         ?? FindMethod(module, "PCL.ModSecret", "get_HasUpdatePermission");
            if (target?.Body == null)
            {
                result.Notes.Add("未找到 HasUpdatePermission，自动更新权限门重写跳过。");
                _log(result.Notes[result.Notes.Count - 1]);
                return;
            }

            var body = new CilBody { KeepOldMaxStack = true, MaxStack = 1 };
            target.Body = body;
            body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
            body.Instructions.Add(OpCodes.Ret.ToInstruction());

            result.Rewrites++;
            result.Notes.Add("ModSecret.HasUpdatePermission -> false（禁止自动更新）");
            _log(result.Notes[result.Notes.Count - 1]);
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
