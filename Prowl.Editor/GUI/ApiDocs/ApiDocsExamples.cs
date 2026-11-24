// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Infinity.Editor.GUI.ApiDocs;

/// <summary>
/// Hand-written examples for the APIs people reach for first. Everything not listed here still gets a generated
/// example from <see cref="ApiDocsCatalog"/>; these are the ones where a real, copyable snippet is worth more.
/// </summary>
internal static class ApiDocsExamples
{
    private static readonly Dictionary<string, string> s_examples = new(StringComparer.Ordinal)
    {
        ["Infinity.Runtime.GameObject"] = """
            using Infinity.Runtime;
            using Infinity.Vector;

            var player = new GameObject("Player");
            player.Transform.Position = new Float3(0, 1, 0);
            player.AddComponent<MyBehaviour>();
            """,

        ["Infinity.Runtime.Component"] = """
            using Infinity.Runtime;
            using Infinity.Vector;

            public class Mover : Component
            {
                public float Speed = 3f;

                public override void Update()
                    => Transform.Position += new Float3(1, 0, 0) * (Speed * Time.DeltaTime);
            }
            """,

        ["Infinity.Vector.Transform"] = """
            using Infinity.Vector;

            Transform.Position = new Float3(0, 2, 0);
            Transform.Rotate(Float3.UnitY, 45f);
            Transform.LookAt(target.Transform.Position);
            """,

        ["Infinity.Runtime.Time"] = """
            float frame = Time.DeltaTime;
            float fixedStep = Time.FixedDeltaTime;
            Time.TimeScale = 0.5f;
            """,

        ["Infinity.Runtime.Input"] = """
            using Infinity.Runtime;

            if (Input.GetKeyDown(KeyCode.Space))
                Debug.Log("Jump");

            if (Input.GetMouseButtonDown(0))
                Debug.Log("Fire");
            """,

        ["Infinity.Runtime.Debug"] = """
            Debug.Log("Loaded");
            Debug.LogWarning("Low health");
            Debug.LogError("Missing reference");
            """,

        ["Infinity.Vector.Maths"] = """
            using Infinity.Vector;

            float clamped = Maths.Clamp(value, 0f, 1f);
            float lerped = Maths.Lerp(0f, 10f, 0.5f);
            """,

        ["Infinity.Runtime.Animation.AnimaTrack"] = """
            using Infinity.Runtime;
            using Infinity.Runtime.Animation;

            var source = GameObject.AddComponent<AnimationPlayer>();
            source.PlayTrack(animaTrack, loop: true);
            source.Completed += player => Debug.Log("Animação terminou");
            """,

        ["Infinity.Runtime.Resources.Flipbook"] = """
            using Infinity.Runtime;
            using Infinity.Runtime.Resources;

            var sprite = GameObject.AddComponent<SpriteAnimation>();
            sprite.Play(flipbook, fromFrame: 0);
            """,

        ["Infinity.Runtime.GameObject.AddComponent"] = """
            var source = gameObject.AddComponent<AnimationPlayer>();
            """,

        ["Infinity.Runtime.GameObject.GetComponent"] = """
            var source = gameObject.GetComponent<AnimationPlayer>();
            if (source != null)
                source.Play();
            """,

        ["Infinity.Runtime.Component.Transform"] = """
            Float3 position = Transform.Position;
            Transform.Position = position + new Float3(0, 1, 0);
            """,

        ["Infinity.Runtime.AnimationPlayer.Play"] = """
            source.Play(animaTrack);
            """,

        ["Infinity.Runtime.AnimationPlayer.CrossFade"] = """
            source.CrossFade(otherTrack, 0.25f);
            """,

        ["Infinity.Runtime.SpriteAnimation.Play"] = """
            sprite.Play(flipbook, fromFrame: 0);
            """,

        ["Infinity.Runtime.Primitive2D"] = """
            using Infinity.Runtime;
            using Infinity.Vector;

            // Cria uma silhueta 2D real (não é um quad esticado) em um objeto novo.
            var shape = new GameObject("Player 2D");
            Primitive2D body = shape.AddComponent<Primitive2D>();
            body.Shape = Primitive2DShape.Capsule;
            body.Size = new Float2(1f, 2f);
            body.Tint = Color.Cyan;
            body.SortingOrder = 10;

            // Um colisor e um corpo físico completam o personagem.
            shape.AddComponent<BoxCollider2D>().Size = body.Size;
            var rb = shape.AddComponent<Rigidbody2D>();
            rb.UseGravity = true;
            rb.GravityScale = 2f;
            """,

        ["Infinity.Runtime.Physics2D"] = """
            using Infinity.Runtime;
            using Infinity.Vector;

            // Consultas em XY, sem depender do solver 3D.
            Float2 origin = new(0f, 1f);
            if (Physics2D.Raycast(origin, new Float2(0f, -1f), 5f, out RaycastHit2D hit2D))
                Debug.Log($"Bateu em {hit2D.GameObject.Name} a {hit2D.Distance:0.00}m");

            Collider2D[] nearby = Physics2D.OverlapCircle(origin, 2f);
            foreach (Collider2D collider in nearby)
                Debug.Log(collider.GameObject.Name);
            """,

        ["Infinity.Runtime.Rigidbody2D"] = """
            using Infinity.Runtime;
            using Infinity.Vector;

            // Movimento lateral e pulo com API de impulso.
            var rb2D = GetComponent<Rigidbody2D>();
            float x = Input.GetAxis("Horizontal");
            rb2D.LinearVelocity = new Float2(x * 6f, rb2D.LinearVelocity.Y);

            if (Input.GetKeyDown(KeyCode.Space))
                rb2D.AddImpulse(new Float2(0f, 8f));
            """,

        ["Infinity.Runtime.SpriteAnimation"] = """
            using Infinity.Runtime;
            using Infinity.Runtime.Resources;

            // Toca um flipbook de 4 direções e troca de clipe sem recriar o renderer.
            SpriteAnimation animation = GetComponent<SpriteAnimation>();
            animation.PlayFlipbook(myFlipbook, useBookSettings: true);
            animation.SetSpeed(14f);
            animation.Loop = true;
            animation.FrameChanged += frame => Debug.Log($"Frame {frame}");

            // Um frame específico, útil para sincronizar ataque com dano.
            animation.PlayFrame(3);
            """,

        ["Infinity.Runtime.AnimationPlayer"] = """
            using Infinity.Runtime;
            using Infinity.Runtime.Animation;

            // A animação é um AnimaTrack; o Animation Source só a toca.
            AnimationPlayer source = GetComponent<AnimationPlayer>();
            source.PlayTrack(walkTrack, loop: true);
            source.CrossFadeTo(runTrack, 0.25f);

            // Amostragem manual para UI, ragdoll ou replay.
            float t = source.NormalizedTime;
            Float3 head = source.SamplePosition("Head", t, Float3.Zero);
            """,

        ["Infinity.Runtime.Graphs.ScriptGraph"] = """
            using Infinity.Runtime;
            using Infinity.Runtime.Graphs;

            // O Script Graph é compilado em C# com o mesmo compilador da engine.
            // Arraste o .scriptgraph para um GameObject: um ScriptGraphRunner é criado.
            ScriptGraphRunner runner = GetComponent<ScriptGraphRunner>();
            runner.Graph = myGraph;
            runner.Reload();
            object? behaviour = runner.Instance; // instância compilada, com os eventos do grafo
            """,

        ["Infinity.Runtime.Graphs.MaterialGraph"] = """
            using Infinity.Runtime;
            using Infinity.Runtime.Graphs;
            using Infinity.Runtime.Resources;

            // O MaterialGraph gera shader Graphite. Post Process = true permite usá-lo em volume.
            MaterialGraph graph = AssetDatabase.Load<MaterialGraph>(graphGuid);
            Material? material = graph.Compile();
            if (material.IsValid()) renderer.Material = material;
            """,

        ["Infinity.Runtime.Rendering.Volume"] = """
            using Infinity.Runtime;
            using Infinity.Runtime.Rendering;

            // Volume local de pós-processamento.
            var volume = new GameObject("Post FX").AddComponent<Volume>();
            volume.IsGlobal = false;
            volume.Size = new Float3(10f, 4f, 10f);
            volume.BlendDistance = 3f;
            volume.Profile.Add<Bloom>().Intensity.Set(0.8f);

            // Para aplicar sem a câmera entrar na caixa, marque no Volume:
            volume.ApplyOutsideBounds = true;

            // Ou, na câmera, para todos os volumes locais:
            Camera camera = GetComponent<Camera>();
            camera.PostProcessing = true;
            camera.ApplyVolumesOutsideTheirBounds = true;
            """,

        ["Infinity.Runtime.Input.GetKeyDown"] = """
            if (Input.GetKeyDown(KeyCode.Space))
                Debug.Log("Jump");
            """,

        ["Infinity.Runtime.Debug.Log"] = """
            Debug.Log("Hello from Infinity Engine");
            """,

        ["Infinity.Vector.Transform.Rotate"] = """
            Transform.Rotate(Float3.UnitY, 45f);
            """,

        ["Infinity.Vector.Transform.LookAt"] = """
            Transform.LookAt(target.Transform.Position);
            """,

        ["Infinity.Services.ServiceManager"] = """
            using Infinity.Services;

            // Register the backends the game uses, then tick them with the game loop.
            var services = ServiceManager.Shared;
            var steam = services.Register(new SteamService { Config = new SteamConfig { AppId = 480 } });
            var server = services.Register(new GameServer { Config = new GameServerConfig { Port = 7777 } });
            services.InitializeAll();

            void Update(float dt) => services.Tick(dt);
            """,

        ["Infinity.Services.GameServer"] = """
            using Infinity.Services;

            var server = new GameServer
            {
                Config = new GameServerConfig
                {
                    Name = "Meu Servidor",
                    Port = 7777,
                    MaxPlayers = 16,
                },
            };
            server.PlayerJoined += player => Debug.Log($"{player.Name} entrou");
            server.MessageReceived += (player, channel, payload) =>
            {
                if (channel == "chat") server.Broadcast("chat", $"{player.Name}: {payload}");
            };
            server.Start();
            """,

        ["Infinity.Services.SteamService"] = """
            using Infinity.Services;

            var steam = new SteamService
            {
                Config = new SteamConfig { AppId = 480, WebApiKey = "SUA_CHAVE" },
                Backend = new MeuSteamworksBackend(), // implemente ISteamBackend
            };
            steam.DefineAchievements("ACH_WIN_ONE_GAME", "ACH_WIN_100_GAMES");
            steam.Initialize();
            steam.UnlockAchievement("ACH_WIN_ONE_GAME");
            """,

        ["Infinity.Services.EpicGamesService"] = """
            using Infinity.Services;

            var epic = new EpicGamesService
            {
                Config = new EpicGamesConfig
                {
                    ProductId = "produto",
                    SandboxId = "sandbox",
                    DeploymentId = "deployment",
                },
                Backend = new MeuEosBackend(), // implemente IEpicOnlineBackend
            };
            epic.Initialize();
            epic.SetPresence("No menu principal");
            """,

        ["Infinity.DIK.FileTools"] = """
            using Infinity.DIK;

            FileTools.WriteText("Saves/slot1.json", "{ \"level\": 3 }");
            string? save = FileTools.ReadText("Saves/slot1.json");
            FileTools.CopyFile("Saves/slot1.json", "Saves/slot1.bak.json");
            bool deleted = FileTools.DeleteFile("Saves/old.json");
            foreach (FileEntry entry in FileTools.List("Saves", "*.json"))
                Debug.Log($"{entry.Name} ({entry.Size} bytes)");
            """,

        ["Infinity.DIK.WindowTools"] = """
            using Infinity.DIK;

            WindowInfo? window = WindowTools.FindByTitle("Bloco de Notas");
            if (window != null)
            {
                WindowTools.SetPosition(window.Handle, 100, 100);
                WindowTools.SetSize(window.Handle, 900, 600);
                WindowTools.TrySaveIconPng(window.Handle, "window-icon.png");
                if (WindowTools.IsMinimized(window.Handle))
                    WindowTools.Restore(window.Handle);
            }
            """,

        ["Infinity.DIK.RegistryTools"] = """
            using Infinity.DIK;

            string? value = RegistryTools.ReadString(
                RegistryHive.CurrentUser, @"Software\MinhaEmpresa\MeuJogo", "InstallPath");
            RegistryTools.WriteString(
                RegistryHive.CurrentUser, @"Software\MinhaEmpresa\MeuJogo", "InstallPath", "C:/Games/MeuJogo");
            RegistryTools.DeleteValue(
                RegistryHive.CurrentUser, @"Software\MinhaEmpresa\MeuJogo", "OldValue");
            """,

        ["Infinity.DIK.DialogTools.Show"] = """
            using Infinity.DIK;

            MessageBoxResult result = DialogTools.Show(
                "Bem-vindo ao Infinity Engine!",
                "Infinity Engine",
                MessageBoxButtons.Ok,
                MessageBoxIcon.Information);
            """,

        ["Infinity.DIK.Dik.MessageBox"] = """
            using Infinity.DIK;

            // O botão afirmativo precisa ser clicado duas vezes antes de fechar.
            Dik.MessageBox(
                "Deseja continuar?",
                "Infinity Engine",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                requiredClicks: 2);
            """,

        ["Infinity.DIK.DllTools"] = """
            using Infinity.DIK;

            // Somente leitura: nomes e versões de DLLs carregadas no processo.
            foreach (DllInfo dll in DllTools.GetLoadedModules())
                Debug.Log($"{dll.Name} {dll.FileVersion} managed={dll.IsManaged}");
            """,

        ["Infinity.DIK.RegistryTools.GetStartupEntries"] = """
            using Infinity.DIK;

            // Lê Run/RunOnce sem alterar nada, para uma tela de sistema realista.
            foreach (RegistryStartupEntry entry in RegistryTools.GetStartupEntries())
                Debug.Log($"{entry.Hive} {entry.Name} = {entry.Value}");
            """,

        ["Infinity.DIK.SecurityScanner"] = """
            using Infinity.DIK;

            SecurityReport report = SecurityScanner.Scan(new SecurityScanOptions
            {
                GameDirectory = AppContext.BaseDirectory,
            });
            Debug.Log($"Críticos: {report.CriticalCount}, avisos: {report.WarningCount}");
            """,

        ["Infinity.DIK.GameSecurityGuard"] = """
            using Infinity.DIK;

            // O jogador vê a lista de arquivos e escolhe SIM (2 cliques) ou NÃO.
            bool run = GameSecurityGuard.ConfirmLaunch(
                new SecurityScanOptions { GameDirectory = AppContext.BaseDirectory },
                requiredClicks: 2);
            """,

        ["Infinity.DIK.HackSimulation"] = """
            using Infinity.DIK;

            using var hack = new HackSimulation(new HackSimulationOptions
            {
                TargetName = "PC-DO-JOGADOR",
                Duration = TimeSpan.FromSeconds(10),
                FinalMessage = "ACESSO CONCEDIDO",
            });
            hack.StageChanged += (index, stage) => Debug.Log($"{index}: {stage.Name}");
            hack.Completed += () => Debug.Log("Sequência concluída");
            hack.Start();
            """,

        ["Infinity.DIK.Dik.SimulateHack"] = """
            using Infinity.DIK;

            using HackSimulation hack = Dik.SimulateHack(new HackSimulationOptions
            {
                Duration = TimeSpan.FromSeconds(6),
            });
            """,

        ["Infinity.DIK.BsodSimulator"] = """
            using Infinity.DIK;

            var bsod = new BsodSimulator(new BsodOptions
            {
                Title = ":( Seu jogo encontrou um problema",
                Duration = TimeSpan.FromSeconds(8),
            });
            bsod.ThresholdReached += percent =>
            {
                if (percent == 50) Debug.Log("Metade do progresso");
            };
            bsod.Completed += () => Debug.Log("Voltou ao normal");
            bsod.Start();
            """,

        ["Infinity.DIK.Dik.SimulateBsod"] = """
            using Infinity.DIK;

            using BsodSimulator bsod = Dik.SimulateBsod(new BsodOptions
            {
                Duration = TimeSpan.FromSeconds(6),
            });
            """,

        ["Infinity.DIK.WindowTools.GetPosition"] = """
            using Infinity.DIK;

            WindowPoint? position = WindowTools.GetPosition(windowHandle);
            if (position is { } point)
                Debug.Log($"Janela em X={point.X}, Y={point.Y}");
            """,

        ["Infinity.DIK.WindowTools.SetBorderless"] = """
            using Infinity.DIK;

            WindowTools.SetBorderless(windowHandle, true);
            WindowTools.SetAlwaysOnTop(windowHandle, true);
            WindowTools.SetOpacity(windowHandle, 230);
            WindowTools.SetClickThrough(windowHandle, true);
            """,

        ["Infinity.DIK.WindowTools.Shake"] = """
            using Infinity.DIK;

            using WindowShake? shake = WindowTools.Shake(windowHandle, intensity: 14, durationMilliseconds: 400);
            """,

        ["Infinity.DIK.Dik.ShakeWindow"] = """
            using Infinity.DIK;

            using WindowShake? shake = Dik.ShakeWindow("Infinity Engine", intensity: 10);
            """,

        ["Infinity.DIK.ScreenTools.CursorPosition"] = """
            using Infinity.DIK;

            WindowPoint? cursor = ScreenTools.CursorPosition;
            if (cursor is { } point)
                ScreenTools.CursorPosition = new WindowPoint(point.X + 10, point.Y);
            """,

        ["Infinity.DIK.OsTools.GetInfo"] = """
            using Infinity.DIK;

            OsInfo os = OsTools.GetInfo();
            Debug.Log($"{os.OsDescription} ({os.Architecture})");
            Debug.Log($"PID {os.ProcessId}, {os.ProcessorCount} threads, uptime {os.UptimeMilliseconds} ms");
            """,

        ["Infinity.DIK.NativeTools"] = """
            using Infinity.DIK;

            nint user32 = NativeTools.Load("user32.dll");
            nint messageBox = NativeTools.GetExport(user32, "MessageBoxW");
            NativeTools.Free(user32);
            """,

        ["Infinity.DIK.SystemInfo"] = """
            using Infinity.DIK;

            ComputerSnapshot pc = SystemInfo.GetSnapshot();
            Debug.Log($"{pc.MachineName} - {pc.OsDescription}");
            Debug.Log($"CPU: {pc.Cpu.Name} ({pc.Cpu.LogicalProcessors} threads)");
            Debug.Log($"RAM: {pc.Memory.TotalBytes / 1024 / 1024} MB");
            foreach (DiskInfo disk in pc.Disks)
                Debug.Log($"{disk.Name} livre: {disk.FreeBytes / 1024 / 1024} MB");
            """,

        ["Infinity.Runtime.Resources.Material"] = """
            using Infinity.Runtime;
            using Infinity.Runtime.Resources;
            using Infinity.Vector;

            var material = new Material(Shader.LoadDefault(DefaultShader.Lit)) { Name = "Chao" };
            material.SetColor("base_color", Color.White);
            material.SetFloat("metallic", 0f);
            material.SetFloat("smoothness", 0.5f);
            renderer.Material = material;
            """,

        ["Infinity.Runtime.Resources.Texture2D"] = """
            using Infinity.Runtime;
            using Infinity.Runtime.Resources;

            // Carrega uma textura do projeto pelo GUID ou caminho.
            Texture2D texture = AssetDatabase.Load<Texture2D>(guid);
            material.SetTexture("base_color_map", texture);
            """,

        ["Infinity.Runtime.Rigidbody3D"] = """
            var body = gameObject.AddComponent<Rigidbody3D>();
            body.Mass = 2f;
            body.AddForce(new Float3(0, 5, 0), ForceMode.Impulse);
            """,

        ["Infinity.Runtime.AudioSource"] = """
            var audio = gameObject.AddComponent<AudioSource>();
            audio.Clip = clip;
            audio.Volume = 0.8f;
            audio.Loop = true;
            audio.Play();
            """,

        ["Infinity.Runtime.Camera"] = """
            var camera = gameObject.AddComponent<Camera>();
            camera.FieldOfView = 60f;
            camera.NearClipPlane = 0.1f;
            camera.FarClipPlane = 1000f;
            """,

        ["Infinity.Runtime.Camera.GetFrustum"] = """
            Frustum frustum = camera.GetFrustum();
            bool seesBox = frustum.Intersects(new AABB(center, size));
            """,

        ["Infinity.Runtime.Camera.IsVisible"] = """
            if (camera.IsVisible(new AABB(center, size)))
                Debug.Log("Dentro da câmera");
            """,

        ["Infinity.Runtime.Jobs"] = """
            float[] values = new float[100_000];
            Jobs.For(0, values.Length, i => values[i] = MathF.Sqrt(i));
            """,

        ["Infinity.Runtime.Jobs.For"] = """
            Jobs.For(0, vertices.Length, i => vertices[i] = TransformPoint(vertices[i]));
            """,
    };

    public static string? For(ApiDocEntry entry)
    {
        string key = entry.IsType ? entry.QualifiedName : entry.OwnerFullName + "." + entry.Name;
        return s_examples.TryGetValue(key, out string? example) ? example : null;
    }
}
