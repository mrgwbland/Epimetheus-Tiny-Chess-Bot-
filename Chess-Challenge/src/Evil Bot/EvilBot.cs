using ChessChallenge.API;
using System.Collections.Generic;
using System;
using System.Numerics;
using System.Linq;

namespace ChessChallenge.Example
{
    public class EvilBot : IChessBot
    {
        // Piece value constants for move ordering
        private static readonly int[] PieceValues = {
        0,      // None
        100,    // Pawn
        310,    // Knight
        320,    // Bishop
        500,    // Rook
        900,    // Queen
        int.MaxValue   // King
    };
        private float Evaluate(Board board)
        {
            if (board.IsInCheckmate())
            {
                return float.NegativeInfinity;
            }

            if (board.IsDraw())
            {
                return 0;
            }

            float whiteScore = 0;
            float blackScore = 0;
            PieceList[] pieceLists = board.GetAllPieceLists();
            int pieceCount = SquareCounter(board.AllPiecesBitboard);

            // Endgame is true if there are less than x pieces left
            int endgame;
            if (pieceCount < 16)
            {
                endgame = 1;
            }
            else
            {
                endgame = -1;
            }

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

            if (board.IsWhiteToMove)
            {
                return whiteScore - blackScore;
            }
            else
            {
                return blackScore - whiteScore;
            }
        }

        private float PieceEvaluator(Board board, Piece piece, int endgame)
        {
            if (piece.IsPawn)
            {
                float pawnValue = 100;
                if (endgame == 1)
                {
                    int rank;
                    if (board.IsWhiteToMove)
                    {
                        rank = piece.Square.Rank;
                    }
                    else
                    {
                        rank = 7 - piece.Square.Rank;
                    }
                    pawnValue += 2 * rank;
                }

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

            // If nothing prior, then the piece must be a king
            return endgame * SquareCounter(BitboardHelper.GetKingAttacks(piece.Square));
        }
        private int SquareCounter(ulong bitboard)
        {
            int count = 0;

            while (bitboard != 0)
            {
                bitboard &= (bitboard - 1);
                count++;
            }

            return count;
        }
        // Estimate move score for ordering
        private int EstimateMoveScore(Board board, Move move)
        {
            int score = 0;

            // Captures - MVV/LVA (Most Valuable Victim / Least Valuable Aggressor)
            if (move.IsCapture)
            {
                Piece capturedPiece = board.GetPiece(move.TargetSquare);
                Piece movingPiece = board.GetPiece(move.StartSquare);

                // Value of captured piece minus value of moving piece (divided by 100)
                int captureValue = GetMaterialValue(capturedPiece) - GetMaterialValue(movingPiece) / 100;
                score += 10000 + captureValue;
            }

            // Promotions
            if (move.IsPromotion)
            {
                score += 9000 + (int)move.PromotionPieceType * 100;
            }

            // Castling
            if (move.IsCastles)
            {
                score += 1000;
            }

            // Check if move gives check (approximation)
            board.MakeMove(move);
            if (board.IsInCheck())
            {
                score += 500;
            }
            board.UndoMove(move);

            return score;
        }

        private int GetMaterialValue(Piece piece)
        {
            if (piece.IsPawn) return PieceValues[1];
            if (piece.IsKnight) return PieceValues[2];
            if (piece.IsBishop) return PieceValues[3];
            if (piece.IsRook) return PieceValues[4];
            if (piece.IsQueen) return PieceValues[5];
            if (piece.IsKing) return PieceValues[6];
            return 0;
        }

        private (float, Move) Negamax(Board board, int depth, float alpha, float beta)
        {
            if (board.IsInCheckmate())
            {
                return (float.NegativeInfinity + (depth * 100), Move.NullMove);
            }

            if (board.IsDraw())
            {
                return (0, Move.NullMove);
            }

            if (depth == 0)
            {
                float finalEval = QuiescenceSearch(board, alpha, beta);
                return (finalEval, Move.NullMove);
            }

            Move[] legalMoves = board.GetLegalMoves();
            Move bestMove = Move.NullMove;
            float bestEval = float.NegativeInfinity;

            // Order moves
            List<(Move move, int score)> scoredMoves = new List<(Move, int)>();
            foreach (Move move in legalMoves)
            {
                int moveScore = EstimateMoveScore(board, move);
                scoredMoves.Add((move, moveScore));
            }
            scoredMoves.Sort((a, b) => b.score.CompareTo(a.score)); // Sort in descending order

            // Process ordered moves
            foreach (var (move, _) in scoredMoves)
            {
                board.MakeMove(move);
                (float eval, _) = Negamax(board, depth - 1, -beta, -alpha);
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
                    break; // Beta cutoff
                }
            }

            return (bestEval, bestMove);
        }

        private float QuiescenceSearch(Board board, float alpha, float beta)
        {
            float standPat = Evaluate(board);

            if (standPat >= beta)
            {
                return beta;
            }

            if (standPat > alpha)
            {
                alpha = standPat;
            }

            // Get captures and order them
            Move[] legalMoves = board.GetLegalMoves();
            List<(Move move, int score)> scoredMoves = new List<(Move, int)>();

            foreach (Move move in legalMoves)
            {
                if (move.IsCapture || board.IsInCheck())
                {
                    int moveScore = EstimateMoveScore(board, move);
                    scoredMoves.Add((move, moveScore));
                }
            }
            scoredMoves.Sort((a, b) => b.score.CompareTo(a.score)); // Sort in descending order

            foreach (var (move, _) in scoredMoves)
            {
                board.MakeMove(move);
                float eval = -QuiescenceSearch(board, -beta, -alpha);
                board.UndoMove(move);

                if (eval >= beta)
                {
                    return beta;
                }

                if (eval > alpha)
                {
                    alpha = eval;
                }
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
                {
                    return move;
                }

                board.UndoMove(move);
            }

            int depth = 4;

            if (SquareCounter(board.AllPiecesBitboard) < 14)
            {
                depth += 1;
            }

            if (SquareCounter(board.AllPiecesBitboard) < 8)
            {
                depth += 2;
            }

            if (SquareCounter(board.AllPiecesBitboard) < 5)
            {
                depth += 2;
            }

            if (board.IsInCheck())
            {
                depth += 1;
            }

            if (timer.MillisecondsRemaining > 60000)
            {
                depth += 1;
            }

            if (timer.MillisecondsRemaining < 5000)
            {
                depth -= 2;
            }

            if (timer.MillisecondsRemaining < 1000)
            {
                depth = 1;
            }

            (float bestScore, Move bestMove) = Negamax(board, depth, float.NegativeInfinity, float.PositiveInfinity);

            return bestMove;
        }
    }
}