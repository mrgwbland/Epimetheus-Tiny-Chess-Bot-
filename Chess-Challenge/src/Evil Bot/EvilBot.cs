using ChessChallenge.API;
using System.Collections.Generic;
using System;
using System.Numerics;
using System.Linq;

namespace ChessChallenge.Example
{
    public class EvilBot : IChessBot
    {
        private float Evaluate(Board board, Timer timer)
        {
            if (board.IsInCheckmate() && board.IsWhiteToMove)
            {
                return float.MinValue;
            }
            if (board.IsInCheckmate() && !board.IsWhiteToMove)
            {
                return float.MaxValue;
            }
            if (board.IsDraw())
            {
                return 0;
            }
            PieceList[] Complete_List = board.GetAllPieceLists();
            float Evaluation = (Complete_List[0].Count * 100)
                       + (Complete_List[1].Count * 300)
                       + (Complete_List[2].Count * 320)
                       + (Complete_List[3].Count * 500)
                       + (Complete_List[4].Count * 900)
                       - (Complete_List[6].Count * 100)
                       - (Complete_List[7].Count * 300)
                       - (Complete_List[8].Count * 320)
                       - (Complete_List[9].Count * 500)
                       - (Complete_List[10].Count * 900);
            return Evaluation;
        }

        private Tuple<float, Move> Search(int Depth_Remaining, float Alpha, float Beta, bool Max_Player, Board board, Timer timer)
        {
            Move[] Moves = board.GetLegalMoves();
            if (Depth_Remaining == 0 || Moves.Length == 0)
            {
                return Tuple.Create(Evaluate(board, timer), Move.NullMove);
            }

            if (Max_Player)
            {
                float Max_Eval = float.MinValue;
                Tuple<float, Move> Best_Move = Tuple.Create(float.MinValue, Move.NullMove);
                foreach (Move Move in Moves)
                {
                    board.MakeMove(Move);
                    float Eval = Search(Depth_Remaining - 1, Alpha, Beta, !Max_Player, board, timer).Item1;
                    board.UndoMove(Move);
                    if (Eval > Max_Eval)
                    {
                        //Console.WriteLine(Move + " Is better");
                        Max_Eval = Eval;
                        Best_Move = Tuple.Create(Eval, Move);
                    }
                    Alpha = MathF.Max(Alpha, Eval);
                    if (Beta <= Alpha)
                    {
                        break;
                    }
                }
                return Best_Move;
            }
            else
            {
                float Min_Eval = float.MaxValue;
                Tuple<float, Move> Best_Move = Tuple.Create(float.MaxValue, Move.NullMove);
                foreach (Move Move in Moves)
                {
                    board.MakeMove(Move);
                    float Eval = Search(Depth_Remaining - 1, Alpha, Beta, !Max_Player, board, timer).Item1;
                    board.UndoMove(Move);
                    if (Eval < Min_Eval)
                    {
                        //Console.WriteLine(Move + " Is better");
                        Min_Eval = Eval;
                        Best_Move = Tuple.Create(Eval, Move);
                    }
                    Beta = MathF.Min(Beta, Eval);
                    if (Beta <= Alpha)
                    {
                        break;
                    }
                }
                return Best_Move;
            }
        }

        public Move Think(Board board, Timer timer)
        {
            return Search(4, float.MinValue, float.MaxValue, board.IsWhiteToMove, board, timer).Item2;
        }
    }
}