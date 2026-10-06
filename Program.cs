using System.Diagnostics;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace RussTechProPatch;

/// <summary>
/// Полный патч Pro для установки RUSS TECH:
/// 1) RussTech.Core.dll      — LicenseCodec.Verify → всегда Pro + отключение обновлений
/// 2) RussTech.Windows.dll   — LicenseVault.Check → всегда Pro
/// 3) RUSS TECH.dll          — LicenseSession.IsPro / Refresh + RequireFeature → всегда true
///                            + UpdateCenterViewModel без проверок/загрузок
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("========================================");
        Console.WriteLine("  RussTech Pro Patch  v1.1.0");
        Console.WriteLine("========================================");
        Console.WriteLine();

        string installDir = args.Length > 0
            ? args[0].Trim('"')
            : AskPath();

        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
        {
            Console.WriteLine("Ошибка: папка установки не найдена.");
            Console.WriteLine("Пример: RussTechProPatch.exe \"E:\\iluma\"");
            return 1;
        }

        string coreDll = Path.Combine(installDir, "RussTech.Core.dll");
        string winDll = Path.Combine(installDir, "RussTech.Windows.dll");
        string appDll = Path.Combine(installDir, "RUSS TECH.dll");

        foreach (var f in new[] { coreDll, winDll, appDll })
        {
            if (!File.Exists(f))
            {
                Console.WriteLine($"Ошибка: не найден файл {Path.GetFileName(f)}");
                Console.WriteLine($"Ожидался путь: {f}");
                return 1;
            }
        }

        Console.WriteLine($"Папка: {installDir}");
        Console.WriteLine("Закрываю процессы RUSS TECH...");
        KillRussTech();

        try
        {
            PatchCore(coreDll);
            PatchWindows(winDll);
            PatchApp(appDll);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("ОШИБКА: " + ex.Message);
            Console.WriteLine("Закройте приложение RUSS TECH и запустите патчер снова.");
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine("Готово. Pro включён, автообновления отключены.");
        Console.WriteLine("Бэкапы: *.prepro рядом с DLL.");
        Console.WriteLine("Запустите: RUSS TECH.exe");
        return 0;
    }

    static string AskPath()
    {
        Console.WriteLine("Укажите папку установки RUSS TECH");
        Console.WriteLine("(где лежат RUSS TECH.exe и RussTech.Core.dll)");
        Console.Write("> ");
        return Console.ReadLine()?.Trim().Trim('"') ?? "";
    }

    static void KillRussTech()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.ProcessName.Contains("RUSS", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  kill PID {p.Id}");
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                }
            }
            catch { /* ignore */ }
        }
        Thread.Sleep(800);
    }

    static void Backup(string path)
    {
        string bak = path + ".prepro";
        if (!File.Exists(bak))
        {
            File.Copy(path, bak, overwrite: true);
            Console.WriteLine($"  backup → {Path.GetFileName(bak)}");
        }
    }

    static void WriteModule(ModuleDefMD mod, string path)
    {
        string tmp = path + ".tmppatch";
        string outAlt = path + ".patched";
        mod.Write(tmp);
        try
        {
            File.Copy(tmp, path, overwrite: true);
            File.Delete(tmp);
        }
        catch (IOException)
        {
            // файл занят — пишем .patched и пробуем заменить через rename
            File.Copy(tmp, outAlt, overwrite: true);
            File.Delete(tmp);
            string old = path + ".oldlive";
            if (File.Exists(old)) File.Delete(old);
            File.Move(path, old);
            File.Move(outAlt, path);
            try { File.Delete(old); } catch { }
        }
    }

    static IMethod FindLicenseCheckCtor(ModuleDefMD mod, int preferParams = 3)
    {
        var local = mod.Types.FirstOrDefault(t => t.FullName == "RussTech.Core.LicenseCheck");
        if (local != null)
        {
            var ctors = local.Methods.Where(m => m.IsConstructor).ToList();
            var hit = ctors.FirstOrDefault(c =>
                c.Parameters.Count == preferParams + (c.HasThis ? 0 : 0) &&
                c.Parameters.Count >= 3 &&
                c.Parameters[1].Type.FullName == "System.Boolean");
            // dnlib: Parameters includes 'this'
            hit ??= ctors.FirstOrDefault(c =>
                c.Parameters.Count >= 3 &&
                c.Parameters[1].Type.FullName == "System.Boolean" &&
                c.Parameters[2].Type.FullName == "System.String");
            if (hit != null) return hit;
        }

        var refs = mod.GetMemberRefs()
            .Where(mr =>
                mr.Name == ".ctor" &&
                mr.DeclaringType?.FullName == "RussTech.Core.LicenseCheck" &&
                mr.MethodSig != null)
            .ToList();

        return refs.FirstOrDefault(mr => mr.MethodSig!.Params.Count == 3)
            ?? refs.FirstOrDefault(mr => mr.MethodSig!.Params.Count >= 2)
            ?? throw new InvalidOperationException("Не найден конструктор LicenseCheck");
    }

    static int CtorValueParams(IMethod ctor)
    {
        if (ctor is MethodDef md)
            return md.Parameters.Count - (md.HasThis ? 1 : 0);
        return ((MemberRef)ctor).MethodSig!.Params.Count;
    }

    static void EmitReturnPro(CilBody body, IMethod ctor)
    {
        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();
        body.Instructions.Add(OpCodes.Ldc_I4_1.ToInstruction());
        body.Instructions.Add(OpCodes.Ldstr.ToInstruction("valid"));
        if (CtorValueParams(ctor) >= 3)
            body.Instructions.Add(OpCodes.Ldnull.ToInstruction());
        body.Instructions.Add(OpCodes.Newobj.ToInstruction(ctor));
        body.Instructions.Add(OpCodes.Ret.ToInstruction());
        body.UpdateInstructionOffsets();
    }

    static void EmitReturnTrue(CilBody body)
    {
        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();
        body.Instructions.Add(OpCodes.Ldc_I4_1.ToInstruction());
        body.Instructions.Add(OpCodes.Ret.ToInstruction());
        body.UpdateInstructionOffsets();
    }

    static void EmitReturnFalse(CilBody body)
    {
        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();
        body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
        body.Instructions.Add(OpCodes.Ret.ToInstruction());
        body.UpdateInstructionOffsets();
    }

    static void EmitThrowIO(MethodDef method, ModuleDefMD mod, string message)
    {
        var ioCtor = mod.Import(typeof(IOException).GetConstructor(new[] { typeof(string) })!);
        method.Body = new CilBody();
        method.Body.Instructions.Add(OpCodes.Ldstr.ToInstruction(message));
        method.Body.Instructions.Add(OpCodes.Newobj.ToInstruction(ioCtor));
        method.Body.Instructions.Add(OpCodes.Throw.ToInstruction());
        method.Body.UpdateInstructionOffsets();
    }

    static void EmitCompletedTask(MethodDef method, ModuleDefMD mod)
    {
        var getCompleted = mod.Import(typeof(Task).GetProperty(nameof(Task.CompletedTask))!.GetMethod!);
        method.Body = new CilBody();
        method.Body.Instructions.Add(OpCodes.Call.ToInstruction(getCompleted));
        method.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
        method.Body.UpdateInstructionOffsets();
    }

    // --- RussTech.Core.dll ---
    static void PatchCore(string path)
    {
        Console.WriteLine();
        Console.WriteLine($"[1/3] RussTech.Core.dll — License + Updates");
        Backup(path);
        var mod = ModuleDefMD.Load(path);
        var codec = mod.Types.FirstOrDefault(t => t.FullName == "RussTech.Core.LicenseCodec")
            ?? throw new InvalidOperationException("LicenseCodec не найден");
        var verify = codec.Methods.FirstOrDefault(m => m.Name == "Verify")
            ?? throw new InvalidOperationException("Verify не найден");
        var ctor = FindLicenseCheckCtor(mod);
        verify.Body ??= new CilBody();
        EmitReturnPro(verify.Body, ctor);
        Console.WriteLine("  OK LicenseCodec.Verify → Pro");

        var prefs = mod.Types.FirstOrDefault(t => t.FullName == "RussTech.Core.UpdatePreferences")
            ?? throw new InvalidOperationException("UpdatePreferences не найден");
        var begin = prefs.Methods.FirstOrDefault(m => m.Name == "BeginStartupCheck")
            ?? throw new InvalidOperationException("BeginStartupCheck не найден");
        begin.Body ??= new CilBody();
        EmitReturnFalse(begin.Body);
        Console.WriteLine("  OK BeginStartupCheck → false");

        var notify = prefs.Methods.FirstOrDefault(m => m.Name == "ShouldNotify")
            ?? throw new InvalidOperationException("ShouldNotify не найден");
        notify.Body ??= new CilBody();
        EmitReturnFalse(notify.Body);
        Console.WriteLine("  OK ShouldNotify → false");

        var client = mod.Types.FirstOrDefault(t => t.FullName == "RussTech.Core.UpdateClient")
            ?? throw new InvalidOperationException("UpdateClient не найден");
        var check = client.Methods.FirstOrDefault(m => m.Name == "Check" && !m.IsStatic)
            ?? throw new InvalidOperationException("UpdateClient.Check не найден");
        EmitThrowIO(check, mod, "updates-disabled");
        Console.WriteLine("  OK UpdateClient.Check → throw");

        var download = client.Methods.FirstOrDefault(m => m.Name == "Download" && !m.IsStatic)
            ?? throw new InvalidOperationException("UpdateClient.Download не найден");
        EmitThrowIO(download, mod, "updates-disabled");
        Console.WriteLine("  OK UpdateClient.Download → throw");

        WriteModule(mod, path);
    }

    // --- RussTech.Windows.dll ---
    static void PatchWindows(string path)
    {
        Console.WriteLine();
        Console.WriteLine($"[2/3] RussTech.Windows.dll — LicenseVault.Check");
        Backup(path);
        var mod = ModuleDefMD.Load(path);
        var vault = mod.Types.FirstOrDefault(t => t.FullName == "RussTech.Windows.LicenseVault")
            ?? throw new InvalidOperationException("LicenseVault не найден");
        var check = vault.Methods.FirstOrDefault(m => m.Name == "Check")
            ?? throw new InvalidOperationException("Check не найден");
        var ctor = FindLicenseCheckCtor(mod);
        check.Body ??= new CilBody();
        EmitReturnPro(check.Body, ctor);
        WriteModule(mod, path);
        Console.WriteLine("  OK → return new LicenseCheck(true, \"valid\")");
    }

    // --- RUSS TECH.dll ---
    static void PatchApp(string path)
    {
        Console.WriteLine();
        Console.WriteLine($"[3/3] RUSS TECH.dll — IsPro / Refresh / RequireFeature");
        Backup(path);
        var mod = ModuleDefMD.Load(path);

        var session = mod.Types.FirstOrDefault(t => t.FullName == "RussTech.App.LicenseSession")
            ?? throw new InvalidOperationException("LicenseSession не найден");

        // get_IsPro → return true
        var getIsPro = session.Methods.FirstOrDefault(m => m.Name == "get_IsPro")
            ?? session.Properties.First(p => p.Name == "IsPro").GetMethod
            ?? throw new InvalidOperationException("get_IsPro не найден");
        getIsPro.Body ??= new CilBody();
        EmitReturnTrue(getIsPro.Body);
        Console.WriteLine("  OK LicenseSession.IsPro → true");

        // Refresh → Current = new LicenseCheck(true, "valid")
        var refresh = session.Methods.First(m => m.Name == "Refresh");
        var setCurrent = session.Properties.First(p => p.Name == "Current").SetMethod
            ?? throw new InvalidOperationException("set_Current не найден");
        var ctor = FindLicenseCheckCtor(mod);
        refresh.Body = new CilBody();
        var il = refresh.Body.Instructions;
        il.Add(OpCodes.Ldarg_0.ToInstruction());
        il.Add(OpCodes.Ldc_I4_1.ToInstruction());
        il.Add(OpCodes.Ldstr.ToInstruction("valid"));
        if (CtorValueParams(ctor) >= 3)
            il.Add(OpCodes.Ldnull.ToInstruction());
        il.Add(OpCodes.Newobj.ToInstruction(ctor));
        il.Add(OpCodes.Call.ToInstruction(setCurrent));
        il.Add(OpCodes.Ret.ToInstruction());
        refresh.Body.UpdateInstructionOffsets();
        Console.WriteLine("  OK LicenseSession.Refresh → Pro");

        // RequireFeature → return true
        var mv = mod.Types.FirstOrDefault(t => t.FullName == "RussTech.App.MainViewModel")
            ?? throw new InvalidOperationException("MainViewModel не найден");
        var req = mv.Methods.First(m => m.Name == "RequireFeature");
        req.Body ??= new CilBody();
        EmitReturnTrue(req.Body);
        Console.WriteLine("  OK RequireFeature → true");

        // Update center — no checks / downloads / startup popups
        var uc = mod.Types.FirstOrDefault(t => t.FullName == "RussTech.App.UpdateCenterViewModel")
            ?? throw new InvalidOperationException("UpdateCenterViewModel не найден");
        var ucCheck = uc.Methods.FirstOrDefault(m =>
                m.Name == "Check" &&
                m.ReturnType.FullName.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("UpdateCenterViewModel.Check не найден");
        EmitCompletedTask(ucCheck, mod);
        Console.WriteLine("  OK UpdateCenterViewModel.Check → CompletedTask");

        var ucDownload = uc.Methods.FirstOrDefault(m =>
                m.Name == "Download" &&
                m.ReturnType.FullName.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal) &&
                m.Parameters.Count == 1)
            ?? throw new InvalidOperationException("UpdateCenterViewModel.Download не найден");
        EmitCompletedTask(ucDownload, mod);
        Console.WriteLine("  OK UpdateCenterViewModel.Download → CompletedTask");

        var showStartup = uc.Methods.FirstOrDefault(m => m.Name == "get_ShowStartupNotifications");
        if (showStartup != null)
        {
            showStartup.Body ??= new CilBody();
            EmitReturnFalse(showStartup.Body);
            Console.WriteLine("  OK ShowStartupNotifications → false");
        }

        WriteModule(mod, path);
    }
}
