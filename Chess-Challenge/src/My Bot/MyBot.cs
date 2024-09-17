using ChessChallenge.API;
using System.Collections.Generic;
using System;
using System.Numerics;
using System.Linq;
using System.IO;

public class MyBot : IChessBot
{
    private float Evaluate(Board board)
    {
        if (board.IsInCheckmate()) return float.NegativeInfinity;
        if (board.IsDraw()) return 0;
        float whiteScore = 0;
        float blackScore = 0;
        PieceList[] pieceLists = board.GetAllPieceLists();
        int piececount = SquareCounter(board.AllPiecesBitboard);
        // Endgame is true if there are less than x pieces left
        int endgame = (piececount < 16) ? 1 : -1;
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
        return board.IsWhiteToMove ? (whiteScore - blackScore) : (blackScore - whiteScore);
    }

    private float PieceEvaluator(Board board, Piece piece, int endgame)
    {
        if (piece.IsPawn)
        {
            float pawnValue = 100 + 2 * (endgame == 1 ? (board.IsWhiteToMove ? piece.Square.Rank : 7 - piece.Square.Rank) : 0);
            return pawnValue;
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
            return 500 + 0.5f * SquareCounter(BitboardHelper.GetSliderAttacks(PieceType.Rook, piece.Square, board));
        }
        if (piece.IsQueen)
        {
            return 900 + 0.1f * SquareCounter(BitboardHelper.GetSliderAttacks(PieceType.Queen, piece.Square, board));
        }
        // If nothing prior then king
        return endgame * SquareCounter(BitboardHelper.GetKingAttacks(piece.Square));
    }

    private int SquareCounter(ulong bitboard)
    {
        // Brian Kernighan's algorithm
        int count = 0;
        while (bitboard != 0)
        {
            bitboard &= (bitboard - 1);
            count++;
        }
        return count;
    }

    private (float, Move) Negamax(Board board, int depth, float alpha, float beta)
    {
        if (board.IsInCheckmate())
        {
            return (float.NegativeInfinity, Move.NullMove);  // Return a null move in checkmate
        }
        if (board.IsDraw())
        {
            return (0, Move.NullMove);  // Return null in a drawn position
        }
        if (depth == 0)
        {
            float finalEval = quiescenceSearch(board, alpha, beta);
            return (finalEval, Move.NullMove);  // Return evaluation with no specific move
        }

        Move[] legalMoves = board.GetLegalMoves();
        Move bestMove = Move.NullMove;  // Track the best move
        float bestEval = float.NegativeInfinity;

        foreach (Move move in legalMoves)
        {
            board.MakeMove(move);
            (float eval, _) = Negamax(board, depth - 1, -beta, -alpha);  // Recursive call to negamax
            eval = -eval;
            board.UndoMove(move);

            if (eval > bestEval)
            {
                bestEval = eval;
                bestMove = move;
            }

            alpha = Math.Max(alpha, eval);
            if (alpha >= beta)
            {
                break;  // Beta cutoff
            }
        }

        return (bestEval, bestMove);
    }


    private float quiescenceSearch(Board board, float alpha, float beta)
    {
        // Evaluation without captures
        float stand_pat = Evaluate(board);
        if (stand_pat >= beta)
        {
            return beta;
        }
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
        Move[] legalMoves = board.GetLegalMoves();
        if (legalMoves.Length == 1)
        {
            return legalMoves[0];
        }
        foreach (Move move in legalMoves)
        {
            board.MakeMove(move);
            if (board.IsInCheckmate())
                return move;
            board.UndoMove(move);
        }
        int depth = 3;
        // Dynamically increase depth based on remaining pieces or timer
        if (SquareCounter(board.AllPiecesBitboard) < 14) depth += 1;
        if (SquareCounter(board.AllPiecesBitboard) < 8) depth += 2;
        if (SquareCounter(board.AllPiecesBitboard) < 5) depth += 2;
        if (board.IsInCheck()) depth += 1;

        if (timer.MillisecondsRemaining < 5000) depth -= 2;
        if (timer.MillisecondsRemaining < 1000) depth = 0;

        // Run Negamax from the starting position, allowing it to return the best move and score
        (float bestScore, Move bestMove) = Negamax(board, depth, float.NegativeInfinity, float.PositiveInfinity);

        return bestMove;  // Return the best move found
    }

    public struct MoveWithScore
    {
        public Move Move { get; set; }
        public float Score { get; set; }
    }
}
