using ChessChallenge.API;
using ChessChallenge.Application;
using ChessChallenge.Application.APIHelpers;
using ChessChallenge.Chess;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChessChallenge.UCI
{
    class UCIBot
    {
        IChessBot bot;
        Chess.Board board;
        APIMoveGen moveGen;
        CancellationTokenSource thinkingCancellationSource;

        static readonly string defaultFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        public UCIBot(IChessBot bot)
        {
            this.bot = bot;
            moveGen = new APIMoveGen();
            board = new Chess.Board();
            thinkingCancellationSource = new CancellationTokenSource();
        }

        void PositionCommand(string[] args)
        {
            int idx = Array.FindIndex(args, x => x == "moves");
            if (idx == -1)
            {
                if (args[1] == "startpos")
                {
                    board.LoadStartPosition();
                }
                else
                {
                    board.LoadPosition(String.Join(" ", args.AsSpan(1, args.Length - 1).ToArray()));
                }
            }
            else
            {
                if (args[1] == "startpos")
                {
                    board.LoadStartPosition();
                }
                else
                {
                    board.LoadPosition(String.Join(" ", args.AsSpan(1, idx - 1).ToArray()));
                }

                for (int i = idx + 1; i < args.Length; i++)
                {
                    // this is such a hack
                    API.Move move = new API.Move(args[i], new API.Board(board));
                    board.MakeMove(new Chess.Move(move.RawValue), false);
                }
            }

            string fen = FenUtility.CurrentFen(board);
            //Console.WriteLine(fen);
        }

        void GoCommand(string[] args)
        {
            // Cancel any previous thinking operation
            thinkingCancellationSource.Cancel();
            thinkingCancellationSource = new CancellationTokenSource();
            var token = thinkingCancellationSource.Token;

            int wtime = 0, btime = 0;
            API.Board apiBoard = new API.Board(board);
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "wtime")
                {
                    wtime = Int32.Parse(args[i + 1]);
                }
                else if (args[i] == "btime")
                {
                    btime = Int32.Parse(args[i + 1]);
                }
                else if (args[i] == "movetime")
                {
                    //To be implemented properly
                    btime = Int32.Parse(args[i + 1])*40;//This is a hack- bot uses /40 of the time given
                    wtime = btime;
                }
                else if (args[i] == "infinite")
                {
                    wtime = int.MaxValue;
                    btime = int.MaxValue;
                }
            }
            if (!apiBoard.IsWhiteToMove)
            {
                int tmp = wtime;
                wtime = btime;
                btime = tmp;
            }
            ChessChallenge.API.Timer timer = new ChessChallenge.API.Timer(wtime, btime, 0);

            MyBot myBot = (MyBot)bot;
            myBot.OnDepthComplete = (depth, eval, pv) =>
            {
                Console.Write("info ");
                Console.Write("depth " + depth + " ");
                Console.Write("score cp " + (int)eval + " ");
                Console.Write("pv " + pv + " ");
                Console.Write("\n");
            };

            // Start thinking in a separate task
            Task.Run(() =>
            {
                try
                {
                    API.Move move = bot.Think(apiBoard, timer);
                    if (!token.IsCancellationRequested)
                    {
                        Console.WriteLine($"bestmove {move.ToString()}");
                    }
                }
                catch (OperationCanceledException)
                {
                    // Thinking was canceled, do nothing
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"info string Error: {ex.Message}");
                }
            }, token);
        }

        void ExecCommand(string line)
        {
            // default split by whitespace
            var tokens = line.Split();

            if (tokens.Length == 0)
                return;

            switch (tokens[0])
            {
                case "help":
                    Console.WriteLine("Available commands: uci, ucinewgame, position, isready, go");
                    break;
                case "uci":
                    Console.WriteLine("id name Chess Challenge");
                    Console.WriteLine("id author George Bland, AspectOfTheNoob, Sebastian Lague");
                    Console.WriteLine("uciok");
                    break;
                case "ucinewgame":
                    bot = new MyBot();
                    break;
                case "position":
                    PositionCommand(tokens);
                    break;
                case "isready":
                    Console.WriteLine("readyok");
                    break;
                case "go":
                    GoCommand(tokens);
                    break;
                case "stop":
                    ((MyBot)bot).MaxTimeForThisMove = 0;
                    thinkingCancellationSource.Cancel();
                    break;
                default:
                    Console.WriteLine("Unknown command: " + tokens[0] + ". Type 'help' for a list of commands.");
                    break;
            }
        }
        public void Run()
        {
            while (true)
            {
                string line = Console.ReadLine();

                if (line == "quit" || line == "exit")
                    return;
                ExecCommand(line);
            }
        }
    }
}