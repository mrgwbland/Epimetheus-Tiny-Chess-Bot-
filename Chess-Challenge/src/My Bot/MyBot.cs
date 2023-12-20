﻿using ChessChallenge.API;
using System.Collections.Generic;
using System;
using System.Numerics;
using System.Linq;
public class MyBot : IChessBot
{
    //int startDepth;
    Dictionary<ulong, float> HashWithScores = new Dictionary<ulong, float>();
    private float Evaluate(Board board)
    {
        if (board.IsInCheckmate()) return float.NegativeInfinity;
        if (board.IsDraw()) return 0;
        float whiteScore = 0;
        float blackScore = 0;
        int endgame = -1;
        PieceList[] pieceLists = board.GetAllPieceLists();
        int piececount = SquareCounter(board.AllPiecesBitboard);
        //Endgame is true if there are less than x pieces left
        //when true endgame = 1
        endgame = (piececount < 16) ? 1 : endgame;
        foreach (PieceList list in pieceLists)
        {
            foreach (Piece piece in list)
            {
                if (piece.IsWhite)
                {
                    whiteScore += PieceEvaluator(board, piece, endgame);
                }
                else
                {
                    blackScore += PieceEvaluator(board, piece, endgame);
                }
            }
        }
        if (board.IsWhiteToMove) return (whiteScore - blackScore);
        return blackScore - whiteScore;
    }
    private float PieceEvaluator(Board board, Piece piece, int endgame)
    {
        if (piece.IsPawn)
        {
            float pawnvalue = 100 + 2 * (endgame == 1 ? (board.IsWhiteToMove ? piece.Square.Rank : 7 - piece.Square.Rank) : 0);
            return pawnvalue;
        }
        if (piece.IsKnight)
        {
            return 310 + SquareCounter(BitboardHelper.GetKnightAttacks(piece.Square));
        }
        if (piece.IsBishop)
        {
            return 320 + SquareCounter(BitboardHelper.GetSliderAttacks(PieceType.Bishop, piece.Square, board));
        }
        if (piece.IsRook)
        {
            return 500 + 0.1f * SquareCounter(BitboardHelper.GetSliderAttacks(PieceType.Rook, piece.Square, board));
        }
        if (piece.IsQueen)
        {
            return 900 + 0.1f * SquareCounter(BitboardHelper.GetSliderAttacks(PieceType.Queen, piece.Square, board));
        }
        //If nothing prior then king
        return endgame * SquareCounter(BitboardHelper.GetKingAttacks(piece.Square));
    }
    private int SquareCounter(ulong bitboard)
    {
        //Brian Kernighan's algorithm
        int count = 0;
        while (bitboard != 0)
        {
            bitboard &= (bitboard - 1);
            count++;
        }
        return count;
    }
    private float Negamax(Board board, int depth, float alpha, float beta)
    {
        if (depth == 0)
        {
            float finalEval = quiescenceSearch(board, alpha, beta);
            HashWithScores[board.ZobristKey] = finalEval; // Store the ZobristKey and its associated score
            //Console.Write("Eval:"+finalEval);
            return finalEval;
        }
        if (board.IsInCheckmate()) return float.NegativeInfinity;
        if (board.IsDraw()) return 0;
        if (HashWithScores.TryGetValue(board.ZobristKey, out float storedScore))
        {
            return storedScore;
        }
        if (alpha >= beta)
        {
            return alpha;
        }
        float bestEval = float.NegativeInfinity;
        Move[] legalMoves = board.GetLegalMoves();
        foreach (Move move in legalMoves)
        {
            board.MakeMove(move);
            //Console.WriteLine();
            //for(int i=depth;i<=startDepth;i++)
            //{
            //    Console.Write("    ");
            //}
            //Console.Write(move);
            float eval = -Negamax(board, depth - 1, -beta, -alpha);
            board.UndoMove(move);
            bestEval = Math.Max(bestEval, eval);
            alpha = Math.Max(alpha, eval);
        }
        return bestEval;
    }
    private float quiescenceSearch(Board board, float alpha, float beta)
    {
        //Evaluation without captures
        float stand_pat = Evaluate(board);
        if (stand_pat >= beta) return beta;
        if (stand_pat > alpha) alpha = stand_pat;
        Move[] legalMoves = board.GetLegalMoves();
        foreach (Move move in legalMoves)
        {
            board.MakeMove(move);
            if (move.IsCapture || board.IsInCheck())
            {
                float eval = -quiescenceSearch(board, -beta, -alpha);
                board.UndoMove(move);
                if (eval >= beta) return eval;
                if (eval < alpha) alpha = eval;
            }
            else board.UndoMove(move);
        }
        return alpha;
    }
    public Move Think(Board board, Timer timer)
    {
        HashWithScores.Clear(); // Clear the dictionary before starting a new search
        int depth = 2;
        List<MoveWithScore> movesWithScores = new List<MoveWithScore>();
        Move[] legalMoves = board.GetLegalMoves();
        //Increases search depth if the following parameters are met.
        if (SquareCounter(board.AllPiecesBitboard) < 14) depth += 1;
        if (SquareCounter(board.AllPiecesBitboard) < 8) depth += 2;
        if (board.IsInCheck()) depth += 1;
        if (legalMoves.Length == 1)
        {
            return legalMoves[0];
        }
        if (timer.MillisecondsRemaining < 5000) depth = 1;
        if (timer.MillisecondsRemaining < 1000) depth = 0;
        //startDepth = depth;
        foreach (Move move in legalMoves)
        {
            //Console.Write("\n"+move);
            if (timer.MillisecondsRemaining < 500)
            {
                movesWithScores = movesWithScores.OrderByDescending(ms => ms.Score).ToList();
                return movesWithScores[0].Move;
            }
            board.MakeMove(move);
            if (board.IsInCheckmate())
            {
                return move;
            }
            float score = -Negamax(board, depth, float.NegativeInfinity, float.PositiveInfinity);
            board.UndoMove(move);
            movesWithScores.Add(new MoveWithScore { Move = move, Score = score });
        }
        movesWithScores = movesWithScores.OrderByDescending(ms => ms.Score).ToList();
        //Console.WriteLine(legalMoves.Length + " legal moves, eval " + movesWithScores[0].Score / 100);
        return movesWithScores[0].Move;
    }
    public struct MoveWithScore
    {
        public Move Move { get; set; }
        public float Score { get; set; }
    }
}