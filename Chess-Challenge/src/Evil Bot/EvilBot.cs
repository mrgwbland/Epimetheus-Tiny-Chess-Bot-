using ChessChallenge.API;
using System.Collections.Generic;
using System;
using System.Numerics;
using System.Linq;

namespace ChessChallenge.Example
{
    public class EvilBot : IChessBot
    {
    public int LastDepth { get; private set; }
        public float LastEvaluation { get; private set; }
        public string LastPV { get; private set; }
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
        private static readonly ulong[] FileMasks = {
    0x0101010101010101, // File A
    0x0202020202020202, // File B
    0x0404040404040404, // File C
    0x0808080808080808, // File D
    0x1010101010101010, // File E
    0x2020202020202020, // File F
    0x4040404040404040, // File G
    0x8080808080808080  // File H
    };
        private float Evaluate(Board board) //Evaluates a single position without depth
        {
            if (board.IsInCheckmate())
            {
                return -99999;
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
            // Count non-pawn pieces
            int nonPawnPieceCount = 0;
            for (int i = 0; i < pieceLists.Length; i++)
            {
                // Skip pawn lists (indices 0 and 6 are white and black pawns)
                if (i != 0 && i != 6)
                {
                    nonPawnPieceCount += pieceLists[i].Count;
                }
            }

            // Endgame is true if there are fewer than 6 non-pawn pieces left
            int endgame;
            if (nonPawnPieceCount < 6)
            {
                endgame = 1;
            }
            else
            {
                endgame = -1;
            }
            // Precompute passed pawns
            HashSet<int> whitePassedPawns = GetPassedPawnSquares(board, true);
            HashSet<int> blackPassedPawns = GetPassedPawnSquares(board, false);
            foreach (PieceList list in pieceLists)
            {
                foreach (Piece piece in list)
                {
                    if (piece.IsWhite)
                    {
                        whiteScore += PieceEvaluator(board, piece, endgame, whitePassedPawns);
                    }
                    else
                    {
                        blackScore += PieceEvaluator(board, piece, endgame, blackPassedPawns);
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

        private float PieceEvaluator(Board board, Piece piece, int endgame, HashSet<int> passedPawns)
        {
            int file = piece.Square.File;
            int rank = piece.Square.Rank;
            bool isWhite = piece.IsWhite;
            float pieceValue = GetMaterialValue(piece);
            if (piece.IsPawn)
            {
                int pawnrank = isWhite ? piece.Square.Rank : 7 - piece.Square.Rank;
                if (endgame == 1)//Reward advanced pawns in the endgame
                {
                    pieceValue += 2 * pawnrank;
                }
                ulong pawnsBB = board.GetPieceBitboard(PieceType.Pawn, piece.IsWhite);

                // Count pawns per file
                int[] pawnsPerFile = new int[8];
                for (int sq = 0; sq < 64; sq++)
                {
                    if (((pawnsBB >> sq) & 1) != 0)
                    {
                        pawnsPerFile[sq % 8]++;
                    }
                }

                int thisFileCount = pawnsPerFile[file];
                // Doubled pawn penalty: -1 for each same colour pawn on the file
                if (thisFileCount > 1)
                    pieceValue -= (thisFileCount - 1);

                // Isolated pawn penalty: -10 if no pawns on adjacent files
                bool hasLeft = file > 0 && pawnsPerFile[file - 1] > 0;
                bool hasRight = file < 7 && pawnsPerFile[file + 1] > 0;
                if (!hasLeft && !hasRight)
                    pieceValue -= 10;

                // Passed pawn bonus
                if (passedPawns.Contains(piece.Square.Index))
                {
                    pieceValue += 4 * pawnrank;
                }

                return pieceValue;
            }

            if (piece.IsKnight)
            {
                return pieceValue + 2f * SquareCounter(BitboardHelper.GetKnightAttacks(piece.Square));
            }
            //GetSliderAttacks() takes blocked squares into account, so it is not necessary to check for blockers here.
            if (piece.IsBishop)
            {
                return pieceValue + 2f * SquareCounter(BitboardHelper.GetSliderAttacks(PieceType.Bishop, piece.Square, board));
            }

            if (piece.IsRook)
            {
                if (endgame == -1)//Non-endgame evaluation for rooks
                {
                    // Reward for squares controlled
                    pieceValue += 1f * SquareCounter(BitboardHelper.GetSliderAttacks(PieceType.Rook, piece.Square, board));

                    // Combine all pawns (both colors)
                    ulong pawnsBB = board.GetPieceBitboard(PieceType.Pawn, true) |
                                    board.GetPieceBitboard(PieceType.Pawn, false);

                    // Get file mask for rook's current file
                    ulong fileMask = FileMasks[piece.Square.File];

                    // Bonus for no pawns on the file
                    if (SquareCounter(pawnsBB & fileMask) == 0)
                        pieceValue += 20f;
                }
                else
                {
                    //Rooks better in the endgame
                    pieceValue += 100;
                }
                return pieceValue;
            }

            if (piece.IsQueen)
            {
                return pieceValue + 0.5f * SquareCounter(BitboardHelper.GetSliderAttacks(PieceType.Queen, piece.Square, board));
            }

            // If nothing prior, then the piece must be a king
            //Prioritises king safety in opening and middlegames
            // If not endgame then favour mobility instead of safety
            // Calculate Manhattan distance to the nearest corner.
            int kingFile = piece.Square.File;
            int kingRank = piece.Square.Rank;
            int distanceA1 = kingFile + kingRank;
            int distanceH1 = (7 - kingFile) + kingRank;
            int distanceA8 = kingFile + (7 - kingRank);
            int distanceH8 = (7 - kingFile) + (7 - kingRank);
            int minDistance = Math.Min(Math.Min(distanceA1, distanceH1), Math.Min(distanceA8, distanceH8));

            // Penalty: the further the king is from a corner, the higher the penalty.
            // Here each square away from safety deducts points.
            float cornerDistance = minDistance * 3;
            return endgame * cornerDistance;
        }
        private HashSet<int> GetPassedPawnSquares(Board board, bool isWhite)
        {
            ulong ownPawns = board.GetPieceBitboard(PieceType.Pawn, isWhite);
            ulong enemyPawns = board.GetPieceBitboard(PieceType.Pawn, !isWhite);

            HashSet<int> passedSquares = new HashSet<int>();

            for (int sq = 0; sq < 64; sq++)
            {
                if (((ownPawns >> sq) & 1) == 0)
                    continue;

                int file = sq % 8;
                int rank = sq / 8;
                bool isPassed = true;

                for (int f = Math.Max(0, file - 1); f <= Math.Min(7, file + 1); f++)
                {
                    if (isWhite)
                    {
                        for (int r = rank + 1; r < 8; r++)
                        {
                            int idx = r * 8 + f;
                            if (((enemyPawns >> idx) & 1) != 0)
                            {
                                isPassed = false;
                                break;
                            }
                        }
                    }
                    else
                    {
                        for (int r = rank - 1; r >= 0; r--)
                        {
                            int idx = r * 8 + f;
                            if (((enemyPawns >> idx) & 1) != 0)
                            {
                                isPassed = false;
                                break;
                            }
                        }
                    }

                    if (!isPassed) break;
                }

                if (isPassed)
                {
                    passedSquares.Add(sq);
                }
            }

            return passedSquares;
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

        private (float, Move, List<Move>) Negamax(Board board, int depth, float alpha, float beta)
        {
            if (board.IsInCheckmate())
            {
                return (-99999 - (depth * 100), Move.NullMove, new List<Move>());
            }

            if (board.IsDraw())
            {
                return (0, Move.NullMove, new List<Move>());
            }

            if (depth == 0)
            {
                float finalEval = QuiescenceSearch(board, alpha, beta);
                return (finalEval, Move.NullMove, new List<Move>());
            }

            Move[] legalMoves = board.GetLegalMoves();
            Move bestMove = Move.NullMove;
            float bestEval = -99999;
            List<Move> bestPV = new();

            // Order moves
            List<(Move move, int score)> scoredMoves = new List<(Move, int)>();
            foreach (Move move in legalMoves)
            {
                int moveScore = EstimateMoveScore(board, move);
                scoredMoves.Add((move, moveScore));
            }
            scoredMoves.Sort((a, b) => b.score.CompareTo(a.score)); // Sort in descending order

            foreach (var (move, _) in scoredMoves)
            {
                board.MakeMove(move);
                (float eval, _, List<Move> pv) = Negamax(board, depth - 1, -beta, -alpha);
                eval = -eval;
                board.UndoMove(move);

                if (eval > bestEval)
                {
                    bestEval = eval;
                    bestMove = move;
                    bestPV = new List<Move> { move };
                    bestPV.AddRange(pv);
                }

                alpha = Math.Max(alpha, eval);

                if (alpha >= beta)
                {
                    break; // Beta cutoff
                }
            }

            return (bestEval, bestMove, bestPV);
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
                if (move.IsCapture || board.IsInCheck() || move.IsPromotion)
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

            // Play a random opening move on move 1 as White
            if (board.IsWhiteToMove && board.GetFenString().StartsWith("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"))
            {
                string[] openingMoves = new string[]
                    {
                "g1f3", // Nf3
                        //"e2e4", // e4
                        //"g2g3", // g3
                        //"d2d4", // d4
                        //"c2c4", // c4
                        //"e2e3", // e3
                        //"c2c3", // c3
                    };
                Random random = new Random();
                return new Move(openingMoves[random.Next(openingMoves.Length)], board);
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

            if (SquareCounter(board.AllPiecesBitboard) < 14)//Adjust depth when less pieces (less moves so deeper search takes the same time)
            {
                depth += -(int)((SquareCounter(board.AllPiecesBitboard) / 10) * (SquareCounter(board.AllPiecesBitboard) / 10)) + 3;
            }

            if (timer.MillisecondsRemaining > 60000)//Think longer when time is over 1 minute
            {
                depth += 1;
            }

            if (timer.MillisecondsRemaining < 5000)//Think less when under 5 seconds
            {
                depth -= 2;
            }

            if (timer.MillisecondsRemaining < 1000)//Play instantly when under 1 second
            {
                depth = 1;
            }

            (float bestScore, Move bestMove, List<Move> pv) = Negamax(board, depth, -99999, 99999);

            LastDepth = depth;
            LastEvaluation = bestScore;
            LastPV = string.Join(" ", pv.Select(m => m.ToString()));

            return bestMove;
        }
    }
}