using ChessChallenge.API;
using System.Collections.Generic;
using System;
using System.Numerics;
using System.Linq;

namespace ChessChallenge.Example
{
    public class EvilBot : IChessBot
    {
        //The public values are accessed by the uci interface to display various information
        public int LastDepth { get; private set; }
        public float LastEvaluation { get; private set; }
        public string LastPV { get; private set; }
        // Delegate for depth completion notifications
        public delegate void DepthCompleteHandler(int depth, float eval, string pv);
        public DepthCompleteHandler OnDepthComplete { get; set; }
        // Basic piece value constants
        private static readonly int[] PieceValues = {
        0,      // None
        100,    // Pawn
        310,    // Knight
        320,    // Bishop
        500,    // Rook
        900,    // Queen
        0   // King
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
        // Time management and search state
        private long NodesVisited;
        private Timer CurrentTimer;
        public int MaxTimeForThisMove { get; set; }

        // Transposition table
        private const int TT_SIZE = 1 << 20; // ~1 million entries
        private TTEntry[] _transpositionTable = new TTEntry[TT_SIZE];

        private struct TTEntry
        {
            public ulong Key;
            public int Depth;
            public float Value;
            public NodeType NodeType;
            public Move BestMove;
        }

        private enum NodeType { Exact, LowerBound, UpperBound }
        // Timeout exception for search termination
        private class TimeoutException : Exception { }

        private void CheckTimeout()
        {
            NodesVisited++;
            if (NodesVisited % 1000 == 0 && CurrentTimer.MillisecondsElapsedThisTurn >= MaxTimeForThisMove)// Check every 1000 nodes
                throw new TimeoutException();
        }
        private float Evaluate(Board board) //Evaluates a single position without depth
        {
            if (board.IsInCheckmate())
            {
                return -100000 + board.PlyCount;
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

            //Position returns eval for the perspective of the side to move
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
                    // Passed pawns are rewarded more for advancing
                    if (passedPawns.Contains(piece.Square.Index))
                    {
                        pieceValue += 4 * pawnrank;
                    }
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
                // Doubled pawn penalty: -x for each same colour pawn on the file
                if (thisFileCount > 1)
                    pieceValue -= 5 * (thisFileCount - 1);

                // Isolated pawn penalty: -10 if no pawns on adjacent files
                bool hasLeft = file > 0 && pawnsPerFile[file - 1] > 0;
                bool hasRight = file < 7 && pawnsPerFile[file + 1] > 0;
                if (!hasLeft && !hasRight)
                    pieceValue -= 10;

                return pieceValue;
            }

            if (piece.IsKnight)
            {
                return pieceValue + 3f * SquareCounter(BitboardHelper.GetKnightAttacks(piece.Square)); //Knights on the rim are grim
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

                    // Figure out if the rook is on an open file
                    ulong pawnsBB = board.GetPieceBitboard(PieceType.Pawn, true) | board.GetPieceBitboard(PieceType.Pawn, false);
                    ulong fileMask = FileMasks[piece.Square.File];

                    // Reward rooks on open files
                    if (SquareCounter(pawnsBB & fileMask) == 0)
                        pieceValue += 50f;
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
            if (endgame == -1)
            {
                pieceValue -= SquareCounter(BitboardHelper.GetSliderAttacks(PieceType.Queen, piece.Square, board));
            }
            //Prioritises king safety in opening and middlegames
            // If not endgame then favour mobility instead of safety
            // Calculate Manhattan distance to the nearest corner.
            int distanceA1 = file + rank;
            int distanceH1 = (7 - file) + rank;
            int distanceA8 = file + (7 - rank);
            int distanceH8 = (7 - file) + (7 - rank);
            int cornerDistance = Math.Min(Math.Min(distanceA1, distanceH1), Math.Min(distanceA8, distanceH8));
            //In the endgame we reverse evaluation- the further the king is from a corner, the better.
            return endgame * cornerDistance * 3;
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

        private int EstimateMoveScore(Board board, Move move)// Estimate move score for ordering
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
            CheckTimeout();

            ulong zobristKey = board.ZobristKey;
            TTEntry ttEntry = _transpositionTable[zobristKey % TT_SIZE];
            bool ttHit = ttEntry.Key == zobristKey;
            Move bestMoveFromTT = ttHit ? ttEntry.BestMove : Move.NullMove;

            // TT cutoffs
            if (ttHit && ttEntry.Depth >= depth)
            {
                if (ttEntry.NodeType == NodeType.Exact)
                    return (ttEntry.Value, ttEntry.BestMove, new List<Move> { ttEntry.BestMove });
                if (ttEntry.NodeType == NodeType.LowerBound && ttEntry.Value >= beta)
                    return (ttEntry.Value, ttEntry.BestMove, new List<Move> { ttEntry.BestMove });
                if (ttEntry.NodeType == NodeType.UpperBound && ttEntry.Value <= alpha)
                    return (ttEntry.Value, ttEntry.BestMove, new List<Move> { ttEntry.BestMove });
            }
            // Check terminal conditions
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

            // Null move pruning
            int reduction = 2;
            if (!board.IsInCheck() && depth >= reduction + 1)
            {
                board.TrySkipTurn();
                (float evalNull, _, _) = Negamax(board, depth - 1 - reduction, -beta, -alpha);
                evalNull = -evalNull;
                board.UndoSkipTurn();

                // If null move is good enough, return beta but continue with normal search
                // Don't return here - we still need to find the actual best move
                if (evalNull >= beta)
                {
                    // You could return here for efficiency, but you'd need to search at least one move
                    // to get a real best move. For now, let's continue with the search.
                }
            }

            // Generate legal moves first to check for terminal positions
            Move[] legalMoves = board.GetLegalMoves();

            // Order moves
            float bestEval = -99999;
            Move bestMove = legalMoves[0];
            List<(Move move, int score)> scoredMoves = new();
            float alphaOriginal = alpha;

            foreach (Move move in legalMoves)
            {
                int moveScore = EstimateMoveScore(board, move);
                scoredMoves.Add((move, moveScore));
            }
            scoredMoves.Sort((a, b) => b.score.CompareTo(a.score));

            List<Move> bestPV = new();

            // Search moves
            foreach (var (move, _) in scoredMoves)
            {
                board.MakeMove(move);
                (float eval, _, List<Move> subPV) = Negamax(board, depth - 1, -beta, -alpha);
                eval = -eval;
                board.UndoMove(move);

                if (eval > bestEval)
                {
                    bestEval = eval;
                    bestMove = move;
                    bestPV = new List<Move> { move };
                    bestPV.AddRange(subPV);
                }

                alpha = Math.Max(alpha, eval);
                if (alpha >= beta)
                {
                    break; // Beta cutoff
                }
            }

            // Store in transposition table only if we found a valid move
            TTEntry newEntry = new TTEntry
            {
                Key = zobristKey,
                Depth = depth,
                Value = bestEval,
                BestMove = bestMove,
                NodeType = bestEval <= alphaOriginal ? NodeType.UpperBound :
                          bestEval >= beta ? NodeType.LowerBound : NodeType.Exact
            };
            _transpositionTable[zobristKey % TT_SIZE] = newEntry;


            return (bestEval, bestMove, bestPV);
        }

        private float QuiescenceSearch(Board board, float alpha, float beta)
        {
            CheckTimeout();
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
                //Detect if move is a check
                bool isCheck = false;
                board.MakeMove(move);
                if (board.IsInCheck())
                {
                    isCheck = true;
                }
                board.UndoMove(move);
                if (move.IsCapture || isCheck || move.IsPromotion || board.IsInCheck())
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
            NodesVisited = 0;
            CurrentTimer = timer;
            MaxTimeForThisMove = timer.MillisecondsRemaining / 40;
            Move[] legalMoves = board.GetLegalMoves();

            Move bestMove = legalMoves[0];
            int depth = 1;
            int maxDepth = 50;
            // Iterative deepening with time management
            while (depth <= maxDepth)
            {
                try
                {
                    (float score, Move move, List<Move> pv) = Negamax(board, depth, -99999, 99999);
                    bestMove = move;
                    LastDepth = depth;
                    LastEvaluation = score;
                    LastPV = string.Join(" ", pv);
                    OnDepthComplete?.Invoke(depth, score, LastPV);
                    depth++;
                }
                catch (TimeoutException)
                {
                    break;
                }
            }
            return bestMove;
        }
    }
}