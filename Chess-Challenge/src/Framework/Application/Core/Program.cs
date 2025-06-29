﻿using Raylib_cs;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System;
using ChessChallenge.API;
using ChessChallenge.UCI;

namespace ChessChallenge.Application
{
    static class Program
    {
        const bool hideRaylibLogs = true;
        static Camera2D cam;

        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "gui")
            {
                Vector2 loadedWindowSize = GetSavedWindowSize();
                int screenWidth = (int)loadedWindowSize.X;
                int screenHeight = (int)loadedWindowSize.Y;

                if (hideRaylibLogs)
                {
                    unsafe
                    {
                        Raylib.SetTraceLogCallback(&LogCustom);
                    }
                }

                Raylib.InitWindow(screenWidth, screenHeight, "Chess Coding Challenge");
                Raylib.SetTargetFPS(60);

                UpdateCamera(screenWidth, screenHeight);

                ChallengeController controller = new();

                while (!Raylib.WindowShouldClose())
                {
                    Raylib.BeginDrawing();
                    Raylib.ClearBackground(new Color(22, 22, 22, 255));
                    Raylib.BeginMode2D(cam);

                    controller.Update();
                    controller.Draw();

                    Raylib.EndMode2D();

                    controller.DrawOverlay();

                    Raylib.EndDrawing();
                }

                Raylib.CloseWindow();

                controller.Release();
                UIHelper.Release();
            }
            else
            {
                StartUCI(args);
            }
        }

        public static void StartUCI(string[] args)
        {
            ChallengeController.PlayerType player;
            if (args.Length == 0)
            {
                Array.Resize(ref args, 2);
                args[1] = "MyBot";
            }
            bool success = Enum.TryParse(args[1], out player);

            if (!success)
            {
                Console.Error.WriteLine($"Failed to start bot with player type {args[1]}");
                return;
            }

            IChessBot? bot = ChallengeController.CreateBot(player);
            if (bot == null)
            {
                Console.Error.WriteLine($"Cannot create bot of type {player.ToString()}");
                return;
            }

            UCIBot uci = new UCIBot(bot);
            uci.Run();
        }

        public static void SetWindowSize(Vector2 size)
        {
            Raylib.SetWindowSize((int)size.X, (int)size.Y);
            UpdateCamera((int)size.X, (int)size.Y);
            SaveWindowSize();
        }

        public static Vector2 ScreenToWorldPos(Vector2 screenPos) => Raylib.GetScreenToWorld2D(screenPos, cam);

        static void UpdateCamera(int screenWidth, int screenHeight)
        {
            cam = new Camera2D();
            cam.target = new Vector2(0, 15);
            cam.offset = new Vector2(screenWidth / 2f, screenHeight / 2f);
            cam.zoom = screenWidth / 1280f * 0.7f;
        }


        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        private static unsafe void LogCustom(int logLevel, sbyte* text, sbyte* args)
        {
        }

        static Vector2 GetSavedWindowSize()
        {
            if (File.Exists(FileHelper.PrefsFilePath))
            {
                string prefs = File.ReadAllText(FileHelper.PrefsFilePath);
                if (!string.IsNullOrEmpty(prefs))
                {
                    if (prefs[0] == '0')
                    {
                        return Settings.ScreenSizeSmall;
                    }
                    else if (prefs[0] == '1')
                    {
                        return Settings.ScreenSizeBig;
                    }
                }
            }
            return Settings.ScreenSizeSmall;
        }

        static void SaveWindowSize()
        {
            Directory.CreateDirectory(FileHelper.AppDataPath);
            bool isBigWindow = Raylib.GetScreenWidth() > Settings.ScreenSizeSmall.X;
            File.WriteAllText(FileHelper.PrefsFilePath, isBigWindow ? "1" : "0");
        }

      

    }


}