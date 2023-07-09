﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChessV1.Stormcloud
{
	/// <summary>
	/// Engine Calculation using the TQA Approach from Chessboard2.cs and Alpha-Beta Pruning
	/// </summary>
	internal partial class Stormcloud3	// Evaluation
	{
		#region Evaluation Weights



		#endregion

		#region Debug_DeleteMe_Unsafe

		public Stormcloud3()
		{
			byte[] position = {
				0xCA, 0xBD, 0xEB, 0xAC,
				0x99, 0x99, 0x99, 0x99,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x11, 0x11, 0x11, 0x11,
				0x42, 0x35, 0x63, 0x24
			};
			double Test_Eval = PositionEvaluation(position, new PositionData() { PositionKey = "", Turn = EvaluationResultWhiteTurn }).Score;
			var moves = GetAllLegalMoves(position, true).Count;
			System.Diagnostics.Debug.WriteLine("D >> Test Eval mat: " + Test_Eval + " | Moves: " + moves);

			StartEvaluationTestSingleThread(position, true);
		}

		// Todo castle, checks, mate

		async void StartEvaluationTestSingleThread(byte[] startPosition, bool isWhitesTurn)
		{
			SearchNode startNode = new SearchNode(startPosition, new PositionData());
			SearchNodes.Enqueue(startNode);
			//StartProcessingNodesSingleThread();
			StartProcessingMultiThread();
		}

		#endregion

		struct EvaluationResult
		{
			public double Score;
			public byte Result;
			public EvaluationResult(double score, byte evalResult)
			{
				this.Score = score;
				this.Result = evalResult;
			}
		}

		EvaluationResult PositionEvaluation(SearchNode Node) => PositionEvaluation(Node.Position, Node.PositionData);
		EvaluationResult PositionEvaluation(byte[] Position, PositionData PositionData)
		{
			double score = 0;

			byte result = (byte)~PositionData.Turn;	// Invert previous turn byte as default result

			bool GameOver = (result & EvalResultGameOverMask) == EvalResultGameOverMask;	// 0110 or 1001 is for turns, so we need to actually check for 1100
			bool Draw = GameOver && ((result & EvalResultDrawMask) == EvaluationResultDraw);
			if (Draw) result = EvaluationResultDraw;
			else if(GameOver) result = (result & EvalResultWhiteMask) != 0 ? EvaluationResultWhiteWon : EvaluationResultBlackWon;

			// ...

			double materialAdvantage = (PositionData.Turn & 0x0F) != 0 ? MaterialEvaluation(Position) : -MaterialEvaluation(Position);

			score += materialAdvantage;

			return new EvaluationResult(score, result);
		}

		/**
		 * Evaluation Results work like this:
		 * First and second bit 1: Game Over
		 *   - Third and fourth bit 0 -> Game Over by draw
		 *   - Third and fourth bit 1 -> Game Over by win
		 * Else:
		 * First and Second bits are 0
		 * Last 4 bits 1: Whites Turn (Also for Game over -> White wins)
		 * Last 4 bits 0: Blacks Turn (Also for Game over -> Black wins)
		 */

		const byte EvalResultGameOverMask = 0xC0; /// 11000000 => 1100 => 12 => C
		const byte EvalResultDrawMask = 0xF0; /// 11110000, but result needs to be 1100 in the first 4 bits. Its just that all 4 bits matter here
		const byte EvalResultWhiteMask = 0x0F; /// 00001111 => 1111 => 15 => F

		public const byte EvaluationResultWhiteTurn = 0x8F;    /// 10011111	(bits 3 and 4 don't matter here, this is just for inverse)
		public const byte EvaluationResultBlackTurn = 0x40;    /// 01101111	(bits 3 and 4 don't matter here, this is just for inverse)
		const byte EvaluationResultWhiteWon = 0xFF;		/// 11111111
		const byte EvaluationResultBlackWon = 0xF0;		/// 11110000
		const byte EvaluationResultDraw = EvalResultGameOverMask;	/// 11000000, but result  Maybe add types of draw later, but it doesnt really matter

		const byte firstHalfMask = 0xF0;	/// First meaning the first 4 bits from the left
		const byte secondHalfMask = 0x0F;

		// Todo OG: private
		public static double MaterialEvaluation(byte[] Position)
		{
			double score = 0.0;
			foreach (byte doublePiece in Position)
			{
				if (IsWhitePieceFirstHalf(doublePiece)) score += BytePieceValue((byte) ((doublePiece & firstHalfMask) >> 4));    // Shift by 4 to shift bits to second half
				else score -= BytePieceValue((byte) ((doublePiece & firstHalfMask) >> 4));

				if (IsWhitePieceSecondHalf(doublePiece)) score += BytePieceValue((byte) (doublePiece & secondHalfMask));
				else score -= BytePieceValue((byte) (doublePiece & secondHalfMask));
			}
			return score;
		}
	}

	partial class Stormcloud3	// Search Algorithm
	{
		private ConcurrentQueue<SearchNode> SearchNodes = new ConcurrentQueue<SearchNode>();	// We're using a queue so that we can use just one, right?
		private ConcurrentDictionary<byte[], double> Temp_InitialMoveScores = new ConcurrentDictionary<byte[], double>();   // Calculating (Live) (just clone when necessary)

		private ConcurrentDictionary<string, double> PositionDataCacheDirectEvaluation = new ConcurrentDictionary<string, double>();

		private byte[] StartPosition;
		private Turn StartTurnColor;

		public Stormcloud3(byte[] Position, Turn CurrentTurnColor)
		{
			this.StartPosition = Position;
			this.StartTurnColor = CurrentTurnColor;
		}

		// ToDo Actually process position keys and values and stuff

		private short TargetDepth = 100, CurrentDepth = 1;

		private void StartProcessingInitialEntry(byte[] startPosition, bool isWhiteStart)
		{
			var InitialMovesWithPositions = GetAllLegalMoveAndResultingPositionPairs(startPosition, isWhiteStart);

			foreach (var v in InitialMovesWithPositions)
			{
				SearchNodes.Enqueue(new SearchNode(v.Result, v.Move));  //, new PositionData(isWhiteStart ? EvaluationResultWhiteTurn : EvaluationResultBlackTurn)
			}
		}

		private async void StartProcessingMultiThread()
		{

			// (Temporary) Code from Stormcloud 2 (TQA)
			// Determine the number of worker tasks based on the available hardware resources
			int workerCount = Math.Min(Environment.ProcessorCount, 20);

			var tasks = new List<Task>();

			for (int i = 0; i < workerCount; i++)
			{
				tasks.Add(Task.Run(() => StartProcessingNodesSingleThread()));
			}

			await Task.WhenAll(tasks); // Wait for all tasks to complete

		}

		private void StartProcessingNodesSingleThread()
		{
			while(SearchNodes.Count > 0 && CurrentDepth <= TargetDepth)
			{
				ProcessNextNode();
			}
		}

		private void ProcessNextNode(bool isNodeOfStartPosition = false, SearchNode node = null)
		{
			if (node == null) SearchNodes.TryDequeue(out node);
			if (node == null) return;

			int depth = node.CurrentDepth;

			if(CurrentDepth <= depth)
			{
				ProcessNewDepth();
			}

			bool NodeTurnColorIsWhite = node.PositionData.Turn == EvaluationResultWhiteTurn;



			var AllNextOpponentMovesAndPositions = GetAllLegalMoveAndResultingPositionPairs(node.Position, !NodeTurnColorIsWhite);
			var OpponentMoveScores = new Dictionary<byte[], double>();//new List<KeyValuePair<byte[], double>>();
			var OpponentMoveFollowUps = new Dictionary<byte[], List<byte[]>>();

			// Evaluate all Opponent moves:

			foreach (var move in AllNextOpponentMovesAndPositions)
			{
				double score = 0;
				var moves = new List<byte[]>();
				foreach (var pos in GetAllLegalMoveAndResultingPositionPairs(move.Result, NodeTurnColorIsWhite))
				{
					score += PositionEvaluation(pos.Result, new PositionData()).Score;
					moves.Add(pos.Move);
				}
				OpponentMoveScores.Add(move.Move, score);
				OpponentMoveFollowUps.Add(move.Move, moves);
			}

			byte[] bestMove = { 0, 0 };
			double maxScore = double.NegativeInfinity;

			foreach (var pair in OpponentMoveScores)
			{
				if (pair.Value > maxScore)
				{
					maxScore = pair.Value;
					bestMove = pair.Key;
				}
			}

			// Move with highest score is at index 0
			SearchNode OpponentNode = node.Result(bestMove);   // Perhaps use an implementation where the already saved new position is used.
			double opponentEval = PositionEvaluation(OpponentNode).Score;

			// Todo rework scores and 

			// Now get and enqueue all new stuff
			foreach (var moves in OpponentMoveFollowUps[bestMove])
			{
				SearchNode node2 = OpponentNode.Result(moves);
				double thisScore = PositionEvaluation(node2).Score;
				// ToDo Cache + Eval + PositionData object rework
				if (!PositionDataCacheDirectEvaluation.ContainsKey(node2.PositionData.PositionKey))
				{
					SearchNodes.Enqueue(node2);
					PositionDataCacheDirectEvaluation.TryAdd(node2.PositionData.PositionKey, thisScore - opponentEval);
				}
				else
				{
					PositionDataCacheDirectEvaluation[node2.PositionData.PositionKey] += thisScore - opponentEval;
				}

				if (node2.InitialMove != null && node2.InitialMove.Length > 1)
				{
					if (Temp_InitialMoveScores.ContainsKey(node2.InitialMove))
						Temp_InitialMoveScores[node2.InitialMove] += thisScore - opponentEval;
					else Temp_InitialMoveScores.TryAdd(node2.InitialMove, thisScore - opponentEval);
				}
			}
		}

		private void WasserEimer(SearchNode kingTookNode)
		{
			throw new NotImplementedException();
		}

		private void ProcessNewDepth()
		{
			short CurrentDepth = this.CurrentDepth;
			this.CurrentDepth++;
			var scores = Temp_InitialMoveScores.ToDictionary(entry => entry.Key, entry => entry.Value);
			if(Temp_InitialMoveScores.Count == 0)
			{
				System.Diagnostics.Debug.WriteLine($"Depth: {CurrentDepth} | Whoops, no moves | Cache Size: {PositionDataCacheDirectEvaluation.Count}");
				return;
			}
			var bestMove = scores.OrderByDescending(value => value.Value).First();
			string move = bestMove.Key == null || bestMove.Key.Length == 0 ? "null" : 
				bestMove.Key[0].ToString("X2") + " -> " + bestMove.Key[1].ToString("X2") + " | " +
				(int) bestMove.Key[0] + " -> " + (int)bestMove.Key[1];
			System.Diagnostics.Debug.WriteLine($"Depth: {CurrentDepth} | BestMove: [ {move} ]");
		}
	}

	struct MoveResultingPositionPair
	{
		public byte[] Move; // key
		public byte[] Result; // value

		public MoveResultingPositionPair(byte[] move, byte[] result)
		{
			Move = move;
			Result = result;
		}
	}

	class SearchNode
	{
		internal byte[] Position;    // See position binary data docs
		// Position data
		SearchNode ParentNode;	// Save pointer to previous node in search tree for forced checkmate backtracking
		internal PositionData PositionData;
		public byte[] InitialMove;
		public int CurrentDepth;

		public SearchNode(byte[] Position, SearchNode parentNode = null)
			: this(Position,
				  new PositionData(true, Stormcloud3.GeneratePositionKey(Position))
				{
					// ToDo Auto-Generate Position Data
				}, parentNode)
		{ }
		public SearchNode(byte[] Position, byte[] initialMove)
			: this(Position,
				  new PositionData(true, Stormcloud3.GeneratePositionKey(Position))
				{
					// ToDo Auto-Generate Position Data
				}, initialMove)
		{ }
		public SearchNode(byte[] Position, PositionData PositionData, SearchNode parentNode = null)
		{
			if (Position == null) this.Position = new byte[32];
			else this.Position = Position;
			this.PositionData = PositionData;
			this.ParentNode = parentNode;
			SetInitialMove();
			if (ParentNode == null) CurrentDepth = 0;
			else CurrentDepth = ParentNode.CurrentDepth + 1;
		}
		public SearchNode(byte[] Position, PositionData PositionData, byte[] initialMove)
		{
			if (Position == null) this.Position = new byte[32];
			else this.Position = Position;
			this.PositionData = PositionData;
			this.ParentNode = null;
			SetInitialMove(initialMove);
			CurrentDepth = 0;
		}
		
		public SearchNode(byte[] Position, byte Turn, string PositionKey, SearchNode parentNode = null)
		: this(Position, Turn == Stormcloud3.EvaluationResultWhiteTurn, PositionKey, parentNode)
		{ }

		public SearchNode(byte[] Position, bool Turn, string PositionKey, SearchNode parentNode = null)
		{
			if (Position == null) this.Position = new byte[32];
			else this.Position = Position;
			this.PositionData = new PositionData(Turn, PositionKey);
			this.ParentNode = parentNode;
			SetInitialMove();
			if (ParentNode == null) CurrentDepth = 0;
			else CurrentDepth = ParentNode.CurrentDepth + 1;
		}

		public SearchNode Result(byte[] move)
		{
			byte[] newPosition = Stormcloud3.ResultingPosition(Position, move);
			PositionData newData = PositionData.Next(newPosition);
			return new SearchNode(newPosition, newData, this);
		}

		void SetInitialMove(byte[] move = null)
		{
			if (move != null) InitialMove = move;
			else if (ParentNode != null) InitialMove = ParentNode.InitialMove;
			else InitialMove = new byte[0];
		}
	}
	
	// Todo update castleing

	internal struct PositionData
	{
		public const byte defaultCastle = 0xFF;

		public byte Turn;
		public bool IsTurnWhite;
		public byte Castle;
		public string PositionKey;

		// Todo check if castle works as intended

		public bool WhiteCastleKingside() => (Castle & (1 << 0)) != 0; // Check if the 1st bit is set
		public bool WhiteCastleQueenside() => (Castle & (1 << 1)) != 0; // Check if the 2nd bit is set
		public bool BlackCastleKingside() => (Castle & (1 << 2)) != 0; // Check if the 3rd bit is set
		public bool BlackCastleQueenside() => (Castle & (1 << 3)) != 0; // Check if the 4th bit is set

		// Set castle
		public void SetWhiteCastleKingside(bool canCastle) => Castle = (byte)(canCastle ? (Castle | (1 << 0)) : (Castle & ~(1 << 0)));
		public void SetWhiteCastleQueenside(bool canCastle) => Castle = (byte)(canCastle ? (Castle | (1 << 1)) : (Castle & ~(1 << 1)));
		public void SetBlackCastleKingside(bool canCastle) => Castle = (byte)(canCastle ? (Castle | (1 << 2)) : (Castle & ~(1 << 2)));
		public void SetBlackCastleQueenside(bool canCastle) => Castle = (byte)(canCastle ? (Castle | (1 << 3)) : (Castle & ~(1 << 3)));

		/// <summary>
		/// 
		/// </summary>
		/// <param name="Turn"></param>
		public PositionData(bool IsTurnWhite, string PositionKey, byte castle = defaultCastle)
		{
			this.IsTurnWhite = IsTurnWhite;
			this.Turn = IsTurnWhite ? Stormcloud3.EvaluationResultWhiteTurn : Stormcloud3.EvaluationResultBlackTurn;
			this.Castle = castle;
			this.PositionKey = PositionKey;
		}

		// Todo castle implement

		public PositionData Next(byte[] newPosition) => Next(Stormcloud3.GeneratePositionKey(newPosition));
		public PositionData Next(string newPositionKey)
		{
			PositionData data = new PositionData(!this.IsTurnWhite, newPositionKey);   // prev: (byte) ~this.Turn for Turn
			return data;
		}
	}

	partial class Stormcloud3	// Piece Values and Legal moves
	{


		#region Legal Moves


		/// <summary>
		/// A list of all legal moves based on the provided position,<br/>
		/// paired with their respective resulting position. <br/> <br/>
		/// First is the move: [0] = From | [1] = To. <br/>
		/// Indexes are 64-based. <br/>
		/// Second is the new position: [0-31]: double-piece byte.
		/// </summary>
		/// <param name="Position"> The Position, a size 32 byte array. </param>
		/// <returns> List of all legal moves paired with their resulting position. </returns>
		private List<MoveResultingPositionPair> GetAllLegalMoveAndResultingPositionPairs(byte[] Position, bool isTurnColorWhite)
		{
			var movePairs = new List<MoveResultingPositionPair>();
			foreach (byte[] move in GetAllLegalMoves(Position, isTurnColorWhite))
			{
				movePairs.Add(new MoveResultingPositionPair(move, ResultingPosition(Position, move)));
			}
			return movePairs;
		}

		/// <summary>
		/// A list of all legal moves based on the provided position. <br/>
		/// Each Move: [0] = From | [1] = To. <br/>
		/// Indexes are 64-based.
		/// </summary>
		/// <param name="Position"> The Position, a size 32 byte array. </param>
		/// <returns> List of all legal moves. </returns>
		private List<byte[]> GetAllLegalMoves(byte[] Position, bool isTurnColorWhite)              // Todo perhaps discard of this? Or move part of the other function in here
		{
			var moves = new List<byte[]>();
			for (byte i = 0; i < 64; ++i)
			{
				moves.AddRange(GetLegalMovesPiece(Position, i, isTurnColorWhite));
			}
			return moves;
		}

		public static List<byte[]> GetLegalMovesPiece(byte[] position, byte pieceLocationIndex, bool isTurnColorWhite)
		{
			byte piece = position[pieceLocationIndex >> 1];
			bool isPieceWhite;
			if ((pieceLocationIndex & 1) == 1)
			{
				isPieceWhite = (piece & 0x08) == 0;	// 4th bit is 0
				piece &= 0x07; // Uneven index => 2nd half
				piece = (byte) (piece << 4);	// Move to first half
			}
			else
			{
				isPieceWhite = (piece & 0x80) == 0; // 4th bit is 0
				piece &= 0x70; // Even index => 1st half
			}
			if(isPieceWhite != isTurnColorWhite) return new List<byte[]>();

			// Todo look for checks
			if (piece == 0x10) return GetLegalMovesPawn(position, pieceLocationIndex, isPieceWhite);
			if (piece == 0x20) return GetLegalMovesKnight(position, pieceLocationIndex, isPieceWhite);
			if (piece == 0x30) return GetLegalMovesBishop(position, pieceLocationIndex, isPieceWhite);
			if (piece == 0x40) return GetLegalMovesRook(position, pieceLocationIndex, isPieceWhite);
			if (piece == 0x50) return GetLegalMovesQueen(position, pieceLocationIndex, isPieceWhite);
			if (piece == 0x60) return GetLegalMovesKing(position, pieceLocationIndex, isPieceWhite);

			return new List<byte[]>();
		}

		/// <summary>
		/// All legal moves of a pawn.
		/// </summary>
		/// <param name="position"> The current position. </param>
		/// <param name="pawnLocationIndex"> The location index of the pawn (64-format). </param>
		/// <param name="isPieceWhite"> If the pawn is white or not. Not really necessary, but the method that calls this already knows so why calculate it again? </param>
		/// <returns> List of all legal moves of this pawn. </returns>
		public static List<byte[]> GetLegalMovesPawn(byte[] position, byte pawnLocationIndex, bool isPieceWhite)	// 0x09 = black pawn (1001)
		{
			List<byte[]> legalMoves = new List<byte[]>();
			// This is not here. => What is not here? I forgot, I'm not gonna lie. If I dont remember, its not important and this comment will be removed.
			// Todo

			// Check if field infront is clear
			byte fieldIndex = (byte) (isPieceWhite ? pawnLocationIndex - 8 : pawnLocationIndex + 8);
			if (IsValidIndex(fieldIndex))
			{
				if (IsFieldEmpty(position[fieldIndex >> 1], (fieldIndex & 1) == 1))
				{
					add(fieldIndex);
					// If first rank, add double
					if (pawnLocationIndex >> 3 == 0x06 && isPieceWhite || pawnLocationIndex >> 3 == 0x01 && !isPieceWhite)       // Loc index: White: 48, 49... - 55: 00110000, 00110001, 00110010,..., so 00110xxx >> 3 = 00000110 = 6   |   Black: 8,9,10,11... - 15 00001000, 00001001, 00001010 - 00001111 -> Mask of 00001xxx
					{
						byte fieldIndexjump = (byte)(isPieceWhite ? fieldIndex - 8 : fieldIndex + 8);
						if (IsFieldEmpty(position[fieldIndex >> 1], (fieldIndex & 1) == 1))
						{
							add(fieldIndexjump);
						}
					}
				}
			}

			void diagonalMove(byte delta)
			{
				byte fieldIndex2 = (byte)(isPieceWhite ? pawnLocationIndex - delta : pawnLocationIndex + delta);
				if (!IsValidIndex(fieldIndex2)) return;
				bool isSecondHalf = (fieldIndex2 & 1) == 1;
				byte piece = position[fieldIndex2 >> 1];
				if (IsSameRank64IndexFormat(fieldIndex, fieldIndex2))
				{
					if (isPieceWhite && IsBlackPiece(piece, isSecondHalf))
					{
						legalMoves.Add(new byte[] { pawnLocationIndex, fieldIndex2 });
					}
					else if (!isPieceWhite && IsWhitePiece(piece, isSecondHalf))
					{
						add(fieldIndex2);
					}
				}
			}

			void add(byte to)
			{
				if (IsEdgeRank64IndexFormat(to))   // We assume its the correct final rank since pawns shouldnt go backwards
				{
					byte colorMask = (byte) (isPieceWhite ? 0x00 : 0x80);
					legalMoves.Add(ofPro(pawnLocationIndex, to, (byte) (0x02 | colorMask)));	// Promotion to Knight
					legalMoves.Add(ofPro(pawnLocationIndex, to, (byte) (0x03 | colorMask)));	// Promotion to Bishop
					legalMoves.Add(ofPro(pawnLocationIndex, to, (byte) (0x04 | colorMask)));	// Promotion to Rook
					legalMoves.Add(ofPro(pawnLocationIndex, to, (byte) (0x05 | colorMask)));	// Promotion to Queen
				}
				else legalMoves.Add(of(pawnLocationIndex, to));
			}

			diagonalMove(7);
			diagonalMove(9);

			return legalMoves;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="position"></param>
		/// <param name="knightLocationIndex">Location Index in 64-format.</param>
		/// <returns></returns>
		public static List<byte[]> GetLegalMovesKnight(byte[] position, byte knightLocationIndex, bool isPieceWhite)
		{
			byte[] possibleMoves = KnightPossibleMoves[knightLocationIndex];
			//																							mask is important so we only judge the color of the pony now
			// What? ->						div index by 2		get last bit to see if first half or 2nd	bit == 1 => uneven index => second half, else first half						=> this works because the other half is definetly 0000 bcs mask
			//																																	then, when that masked with 10001000 (first and 2nd half black) yields 0, that means the horse is white.
			//bool IsKnightWhite = IsWhitePiece(position[knightLocationIndex >> 1], (knightLocationIndex & 1) == 1); -> perhaps keep for now

			// This seems buggy
			// Todo

			List<byte[]> legalMoves = new List<byte[]>();
			foreach (byte possibleMove in possibleMoves)
			{
				// Check if piece is of own color
				//if((knightLocationIndex & 0x01) == 0x01 ? IsPieceWhite2ndHalf(position[move >> 1]) : IsPieceWhite1stHalf(position[move >> 1]) != IsKnightWhite)

				bool isSecondHalf = (possibleMove & 1) == 1;
				// Check if the target field is empty
				if (IsFieldEmpty(position[knightLocationIndex >> 1], isSecondHalf))
				{
					legalMoves.Add(new byte[2] { knightLocationIndex, possibleMove });
				}
				// If not, check if the colors are different. This might not be the prettiest, but its the fastest
				else if (IsWhitePiece(position[possibleMove >> 1], isSecondHalf) != isPieceWhite)
				{
					legalMoves.Add(new byte[2] { knightLocationIndex, possibleMove });
				}
			}
			return legalMoves;
		}

		public static List<byte[]> GetLegalMovesBishop(byte[] position, byte bishopLocationIndex, bool isPieceWhite)
			=> GetLegalMovesBishop(position, bishopLocationIndex, isPieceWhite, (bishopLocationIndex & 1) == 1);
		public static List<byte[]> GetLegalMovesBishop(byte[] position, byte bishopLocationIndex, bool isPieceWhite, bool isSecondHalf)
		{
			List<byte[]> legalMoves = new List<byte[]>();

			byte index = bishopLocationIndex;
			bool isSecondHalf2 = isSecondHalf;
			while (true)
			{
				if (IsFileH(index)) break;
				if (IsRank8(index)) break;
				index -= 7;
				isSecondHalf2 = !isSecondHalf2;
				addIfLegal(isSecondHalf);
			}

			index = bishopLocationIndex;
			isSecondHalf2 = isSecondHalf;
			while (true)
			{
				if (IsFileA(index)) break;
				if (IsRank8(index)) break;
				index -= 9;
				isSecondHalf2 = !isSecondHalf2;
				addIfLegal(isSecondHalf);
			}

			index = bishopLocationIndex;
			isSecondHalf2 = isSecondHalf;
			while (true)
			{
				if (IsFileA(index)) break;
				if (IsRank1(index)) break;
				index += 7;
				isSecondHalf2 = !isSecondHalf2;
				addIfLegal(isSecondHalf);
			}

			index = bishopLocationIndex;
			isSecondHalf2 = isSecondHalf;
			while (true)
			{
				if (IsFileH(index)) break;
				if (IsRank1(index)) break;
				index += 9;
				isSecondHalf2 = !isSecondHalf2;
				addIfLegal(isSecondHalf);
			}

			void addIfLegal(bool secondHalf)
			{
				byte piece2 = position[index >> 1];
				if (isPieceWhite && !IsWhitePiece(piece2, secondHalf))
				{
					legalMoves.Add(of(bishopLocationIndex, index));
				}
				else if (!isPieceWhite && !IsBlackPiece(piece2, secondHalf))
				{
					legalMoves.Add(of(bishopLocationIndex, index));
				}
			}


			return legalMoves;
		}

		public static List<byte[]> GetLegalMovesRook(byte[] position, byte rookLocationIndex, bool isPieceWhite)
			=> GetLegalMovesRook(position, rookLocationIndex, isPieceWhite, (rookLocationIndex & 1) == 1);
		public static List<byte[]> GetLegalMovesRook(byte[] position, byte rookLocationIndex, bool isPieceWhite, bool isSecondHalf)
		{
			List<byte[]> legalMoves = new List<byte[]>();

			byte index = rookLocationIndex;
			while (true)
			{
				if (IsRank8(index)) break;
				index -= 8;
				addIfLegal(isSecondHalf);
			}

			index = rookLocationIndex;
			while (true)
			{
				if (IsRank1(index)) break;
				index += 8;
				addIfLegal(isSecondHalf);
			}

			index = rookLocationIndex;
			bool isSecondHalf2 = isSecondHalf;
			while (true)
			{
				if (IsFileA(index)) break;
				index -= 1;
				isSecondHalf2 = !isSecondHalf2;
				addIfLegal(isSecondHalf);
			}

			index = rookLocationIndex;
			isSecondHalf2 = isSecondHalf;
			while (true)
			{
				if (IsFileH(index)) break;
				index += 1;
				isSecondHalf2 = !isSecondHalf2;
				addIfLegal(isSecondHalf);
			}

			void addIfLegal(bool secondHalf)
			{
				byte piece2 = position[index >> 1];
				if (isPieceWhite && !IsWhitePiece(piece2, secondHalf))
				{
					legalMoves.Add(of(rookLocationIndex, index));
				}
				else if (!isPieceWhite && !IsBlackPiece(piece2, secondHalf))
				{
					legalMoves.Add(of(rookLocationIndex, index));
				}
			}

			return legalMoves;
		}

		public static List<byte[]> GetLegalMovesQueen(byte[] position, byte queenLocationIndex, bool isPieceWhite)
		{
			List<byte[]> legalMoves = new List<byte[]>();
			bool isSecondHalf = (queenLocationIndex & 1) == 1;

			legalMoves.AddRange(GetLegalMovesBishop(position, queenLocationIndex, isPieceWhite, isSecondHalf));
			legalMoves.AddRange(GetLegalMovesRook(position, queenLocationIndex, isPieceWhite, isSecondHalf));

			return legalMoves;
		}

		public static List<byte[]> GetLegalMovesKing(byte[] position, byte kingLocationIndex, bool isPieceWhite)
		{
			// Debug / Test stuff
			//position = (byte[])position.Clone();
			//System.Diagnostics.Debug.WriteLine($"Index: {kingLocationIndex} | {Convert.ToString((byte) (kingLocationIndex << 5), 2)} | {((byte)(kingLocationIndex << 5)).ToString("X2")}");
			List<byte[]> legalMoves = new List<byte[]>();
			bool isSecondHalf = (kingLocationIndex & 1) == 1;

			if(!IsRank8(kingLocationIndex))
			{
				// Look for moves above
				deltaSameRankUp(9);

				byte i = (byte)(kingLocationIndex - 8);
				addIfLegal(i, isSecondHalf);

				deltaSameRankUp(7);
			}
			if(!IsRank1(kingLocationIndex))
			{
				// Look for moves below
				deltaSameRankDown(9);
				
				byte i = (byte)(kingLocationIndex + 8);
				addIfLegal(i, isSecondHalf);
				
				deltaSameRankDown(7);
			}
			if(!IsFileA(kingLocationIndex))
			{
				//System.Diagnostics.Debug.WriteLine($"Index");
				// Check move to the right
				byte i = (byte)(kingLocationIndex - 1);
				addIfLegal(i, !isSecondHalf);	// No need to check, since the corners are the ones we need to check
			}
			if(!IsFileH(kingLocationIndex))
			{
				// Check move to the right
				byte i = (byte)(kingLocationIndex + 1);
				addIfLegal(i, !isSecondHalf);	// No need to check, since the corners are the ones we need to check
			}

			void deltaSameRankUp(byte delta)
			{
				byte i = (byte) (kingLocationIndex - delta);
				if (IsSameRank64IndexFormat((byte) (kingLocationIndex - 8), i))
					addIfLegal(i, !isSecondHalf);
			}
			void deltaSameRankDown(byte delta)
			{
				byte i = (byte) (kingLocationIndex + delta);
				if (IsSameRank64IndexFormat((byte)(kingLocationIndex + 8), i))
					addIfLegal(i, !isSecondHalf);
			}

			void addIfLegal(byte index, bool secondHalf)
			{
				byte piece2 = position[index >> 1];
				if(isPieceWhite && !IsWhitePiece(piece2, secondHalf))
				{
					legalMoves.Add(of(kingLocationIndex, index));
				}
				else if(!isPieceWhite && !IsBlackPiece(piece2, secondHalf))
				{
					legalMoves.Add(of(kingLocationIndex, index));
				}
			}

			return legalMoves;
		}

		#region Helper Methods | Board Index Analysis

		// Check if the first bit of each half is 0 (indicates white piece)
		static bool IsWhitePiece(byte piece) => (piece & 0x88) == 0 && (piece & 0xFF) != 0x00 /* not empty */;
		static bool IsWhitePiece(byte piece, bool isSecondHalf) => isSecondHalf ? IsWhitePieceFirstHalf(piece) : IsWhitePieceSecondHalf(piece);
		static bool IsWhitePieceFirstHalf(byte piece) => (piece & 0x80) == 0 && (piece & 0xF0) != 0;
		static bool IsWhitePieceSecondHalf(byte piece) => (piece & 0x08) == 0 && (piece & 0x0F) != 0;
		static bool IsBlackPiece(byte piece) => (piece & 0x88) != 0 && (piece & 0xFF) != 0x00 /* not empty */;
		static bool IsBlackPiece(byte piece, bool isSecondHalf) => isSecondHalf ? IsBlackPieceFirstHalf(piece) : IsBlackPieceSecondHalf(piece);
		static bool IsBlackPieceFirstHalf(byte piece) => (piece & 0x80) == 0x80 && (piece & 0xF0) != 0;
		static bool IsBlackPieceSecondHalf(byte piece) => (piece & 0x08) == 0x08 && (piece & 0x0F) != 0;
		static bool IsFieldEmpty(byte piece, bool isSecondHalf) => isSecondHalf ? IsFieldEmptyFirstHalf(piece) : IsFieldEmptySecondHalf(piece);
		static bool IsFieldEmptyFirstHalf(byte piece) => (piece & 0xF0) == 0;
		static bool IsFieldEmptySecondHalf(byte piece) => (piece & 0x0F) == 0;
		static bool IsOppositeColorOrEmpty(byte ogPiece, bool isOGsecondHalf, byte targetPiece, bool isTargetSecondHalf)
		{
			if (IsFieldEmpty(targetPiece, isTargetSecondHalf)) return true;
			if (IsWhitePiece(ogPiece, isOGsecondHalf) && IsWhitePiece(targetPiece, isTargetSecondHalf)) return false;
			if (IsBlackPiece(ogPiece, isOGsecondHalf) && IsBlackPiece(targetPiece, isTargetSecondHalf)) return false;
			return true;
		}
		private static byte[] of(byte from, byte to) => new byte[] { from, to };
		private static byte[] ofPro(byte from, byte to, byte newPiece) => new byte[] { from, to, newPiece };

		private static bool IsSameRank64IndexFormat(byte i1, byte i2)
		{
			return i1 >> 3 == i2 >> 3;  // last 3 bits is position inside rank
		}

		private static bool IsEdgeFile64IndexFormat(byte posIndex)
		{
			// edge is 00000000 or 00001000 or 00010000 or 00011000 or 00100000 => xxxxx000 or xxxxx111 since 111 is position inside rank
			posIndex <<= 5;	// no need to cast because posIndex is byte
			// Check if 11100000 or 00000000
			return posIndex == 0 || posIndex == 0xE0;   // last 3 bits is position inside rank
		}

		private static bool IsEdgeRank64IndexFormat(byte posIndex)
		{
			posIndex >>= 3;
			// Check if 00000000 or 00000111
			return posIndex == 0 || posIndex == 0x07;   // "first" 3 bits is rank
		}

		private static bool IsRank8(byte posIndex) => (posIndex >> 3) == 0;
		private static bool IsRank1(byte posIndex) => (posIndex >> 3) == 7;
		private static bool IsFileA(byte posIndex) => ((byte) (posIndex << 5)) == 0;
		private static bool IsFileH(byte posIndex) => ((byte) (posIndex << 5)) == 0xE0;
		private static bool IsValidIndex(byte posIndex) => (posIndex & 0xC0) == 0;		// Mask 1100 0000 != 0 means >= 64 means invalid index

		// Rank: index >> 3		| => First half
		// File: index & 0x07	| => Second (feinschliff) half, but index is only size 6 (0-64, byte is 0-256)

		#endregion

		#endregion


		internal static string GeneratePositionKey(byte[] position)
		{
			if (position == null) return "null";
			StringBuilder key = new StringBuilder(position.Length * 2);	// String += is inefficient bcs immutable strings
			foreach (byte b in position) key.Append(b.ToString("X2"));	// Since every square is accounted for and every piece has its own Hex character (4 bits), this should provide a unique key in the most effiecient way possible
			return key.ToString();
		}


		// Layout for adding pawn moves (pseudo code):
		//	if (inFront is Clear)
		//	{
		//		addMove (Walk_Forward);
		//		if (rank == 1 (0-based) AND inFront (2) is Clear)
		//		{
		//			addMove (Walk_Forward_Two);
		//		}
		//	}

		/// <summary>
		/// piece in Hex, first byte
		/// </summary>
		/// <param name="Position"></param>
		/// <param name="move"></param>
		/// <param name="differentPiece0xX0"></param>
		/// <returns></returns>
		public static byte[] ResultingPosition(byte[] Position, byte[] move)
		{
			// Input Validation not needed, as this is an internal process

			byte fromIndex = (byte) (move[0] >> 1); // equivalent to move[0] / 2
			byte toIndex = (byte) (move[1] >> 1);   // equivalent to move[1] / 2

			byte fromByte = Position[fromIndex];
			byte toByte = Position[toIndex];
			byte piece;

			if ((move[0] & 1) == 1)     // equivalent to move[0] % 2 == 1 => uneven number, second half is piece, erase piece, so erase 2nd half
			{
				// promotion piece is stored at #3 of the move array, if it exists
				if(move.Length == 2) piece = (byte) (fromByte & secondHalfMask);
				else piece = (byte) (move[2] | (byte) ((fromByte & 0x08) << 4));	// add color bit of fromByte for piece color (2nd half since there is the piece)
				fromByte &= firstHalfMask; // Keep only first 4 bits
			}
			else
			{
				if (move.Length == 2) piece = (byte) ((fromByte & firstHalfMask) << 4);
				else piece = (byte)(move[2] | (byte) ((fromByte & 0x80) << 4)); // add color bit of fromByte for piece color (1st half since there is the piece)		1000 mask to get color bit and then do an or to maybe apply it
				fromByte &= secondHalfMask; // Keep only second 4 bits => erase first 4 bits
											//fromByte >>= bitsPerHalf; // Shift right by 4 bits to keep only first 4 bits			=> this is still from inverted version
			}

			// piece is xxxx0000

			if ((move[1] & 1) == 1)     // equivalent to move[1] % 2 == 1
			{
				toByte &= firstHalfMask; // Keep only first 4 bits
				toByte += piece;
			}
			else
			{
				toByte &= secondHalfMask; // Keep only second 4 bits => erase first 4 bits
										  //toByte >>= bitsPerHalf; // Shift right by 4 bits to keep only first 4 bits
				toByte += (byte) (piece >> 4);
			}

			byte[] newPosition = (byte[])Position.Clone();
			newPosition[fromIndex] = fromByte;
			newPosition[toIndex] = toByte;

			// remove previous en passants

			// If first bit of piece is 1, its black. So, we need to check the white rank (3), since their en passant "expires" now.
			byte mask1, mask2, start;
			if ((piece & 0x80) == 0x80)	// If piece is 1xxx, meaning black
			{
				// Set values for white rank:
				mask1 = 0x70;
				mask2 = 0x07;
				// 3rd rank is en passant black, which is 1111 or 0x0F
				start = 20;
			}
			else
			{
				// Set values for black rank:
				mask1 = 0xF0;
				mask2 = 0x0F;
				// 3rd rank is en passant black, which is 1111 or 0x0F
				start = 8;
			}
			
			for (byte i = start; i < start+4; ++i)   // Indexes 8 - 11 and 20 - 23 (3rd rank and 3rd to last rank)
			{
				byte _piece = newPosition[i];
				if ((_piece & mask1) == mask1) { newPosition[i] &= secondHalfMask; break; }	// If first half is en passant, erase the first half
				if ((_piece & mask2) == mask2) { newPosition[i] &= firstHalfMask; break; } // If second half is en passant, erase the second half
			}

			// Add en passant
			if((piece & 0x70) == 1)	// Pawn
			{
				// Its always +/- 16
				// Check if they are the same without the 16er-bit => 11101111 => 0xEF
				if((move[0] & 0xEF) == (move[1] & 0xEF) && move[0] != move[1])
				{
					// moved by 16 (2 ranks) and is a pawn => Insert en passant
					byte enPassantIndex = (byte) ((fromIndex + toIndex) >> 1);	// Average of the indexes
					
					// Mask is what we're going to keep, so if its the second half (uneven index) we want the first half masked
					if((move[0] & 1) == 1)
					{
						// Clear Index not necessary as its guaranteed empty
						// add en passant to 2nd part of the byte and add the coloring bit if piece is also that color
						newPosition[enPassantIndex] += (byte) (0x07 + ((piece >> 4) & 0x08));
					}
					else
					{
						newPosition[enPassantIndex] += (byte)(0x70 + (piece & 0x80));
					}
				}
			}

			return newPosition;
		}



		private static double BytePieceValue(byte piece2ndHalf)
			=> BytePieceValues[piece2ndHalf & 0x07];	// Only last 3 bits => Mask 00000111 => 7
		private static double[] BytePieceValues =
		{
			// Value, Representation	| Hex  | 2nd Bit Half | Hex Value | Hex Value (Black)
			0,		// Empty			| 0x00 | 000		  | 0		  | (8)
			1,		// Pawn				| 0x01 | 001		  | 1		  | 9
			3,		// Knight			| 0x02 | 010		  | 2		  | A
			3,		// Bishop			| 0x03 | 011		  | 3		  | B
			5,		// Rook				| 0x04 | 100		  | 4		  | C
			9,		// Queen			| 0x05 | 101		  | 5		  | D
			999,	// King				| 0x06 | 110		  | 6		  | E
			1,		// En Passant		| 0x07 | 111		  | 7		  | F
		};


		/// <summary>
		/// KnightPossibleMoves[KnightPositionByte as index] returns AllMoveDestinationsAsBytes[] <br/>
		/// Move Destinations are a 
		/// </summary>
		public static byte[][] KnightPossibleMoves =
		{
			// To override half a byte:
			// byte originalValue = ...; // some original value
			// byte newValueForHigher4Bits = ...; // This should have lower 4 bits as 0.
			// result = (byte)((originalValue & 0x0F) | newValueForHigher4Bits) or (byte)((originalValue & 0xF0) | newValueForLower4Bits)

			// A pinned knight has no moves, an unpinned knight will always have these moves (except if there is an own piece standing there)

			//{ (byte)((0xFF & 0x0F) | newValueForHigher4Bits) },
			// From Field 0: 10, 17
			new byte[2] { 10, 17 },
			// From Field 1: 11, 16, 18
			new byte[3] { 11, 16, 18 },
			// From Field 2: 8, 12, 17, 19
			new byte[4] { 8, 12, 17, 19 },
			// From Field 3: 9, 13, 18, 20
			new byte[4] { 9, 13, 18, 20 },
			// From Field 4: 10, 14, 19, 21
			new byte[4] { 10, 14, 19, 21 },
			// From Field 5: 11, 15, 20, 22
			new byte[4] { 11, 15, 20, 22 },
			// From Field 6: 12, 21, 23
			new byte[3] { 12, 21, 23 },
			// From Field 7: 13, 22
			new byte[2] { 13, 22 },
			// From Field 8: 2, 18, 25
			new byte[3] { 2, 18, 25 },
			// From Field 9: 3, 19, 24, 26
			new byte[4] { 3, 19, 24, 26 },
			// From Field 10: 0, 4, 16, 20, 25, 27
			new byte[6] { 0, 4, 16, 20, 25, 27 },
			// From Field 11: 1, 5, 17, 21, 26, 28
			new byte[6] { 1, 5, 17, 21, 26, 28 },
			// From Field 12: 2, 6, 18, 22, 27, 29
			new byte[6] { 2, 6, 18, 22, 27, 29 },
			// From Field 13: 3, 7, 19, 23, 28, 30
			new byte[6] { 3, 7, 19, 23, 28, 30 },
			// From Field 14: 4, 20, 29, 31
			new byte[4] { 4, 20, 29, 31 },
			// From Field 15: 5, 21, 30
			new byte[3] { 5, 21, 30 },
			// From Field 16: 1, 10, 26, 33
			new byte[4] { 1, 10, 26, 33 },
			// From Field 17: 0, 2, 11, 27, 32, 34
			new byte[6] { 0, 2, 11, 27, 32, 34 },
			// From Field 18: 1, 3, 8, 12, 24, 28, 33, 35
			new byte[8] { 1, 3, 8, 12, 24, 28, 33, 35 },
			// From Field 19: 2, 4, 9, 13, 25, 29, 34, 36
			new byte[8] { 2, 4, 9, 13, 25, 29, 34, 36 },
			// From Field 20: 3, 5, 10, 14, 26, 30, 35, 37
			new byte[8] { 3, 5, 10, 14, 26, 30, 35, 37 },
			// From Field 21: 4, 6, 11, 15, 27, 31, 36, 38
			new byte[8] { 4, 6, 11, 15, 27, 31, 36, 38 },
			// From Field 22: 5, 7, 12, 28, 37, 39
			new byte[6] { 5, 7, 12, 28, 37, 39 },
			// From Field 23: 6, 13, 29, 37
			new byte[4] { 6, 13, 29, 37 },
			// From Field 24: 9, 18, 34, 41
			new byte[4] { 9, 18, 34, 41 },
			// From Field 25: 8, 10, 19, 35, 40, 42
			new byte[6] { 8, 10, 19, 35, 40, 42 },
			// From Field 26: 9, 11, 16, 20, 32, 36, 41, 43
			new byte[8] { 9, 11, 16, 20, 32, 36, 41, 43 },
			// From Field 27: 10, 12, 17, 21, 33, 37, 42, 44
			new byte[8] { 10, 12, 17, 21, 33, 37, 42, 44 },
			// From Field 28: 11, 13, 18, 22, 34, 38, 43, 45
			new byte[8] { 11, 13, 18, 22, 34, 38, 43, 45 },
			// From Field 29: 12, 14, 19, 23, 35, 39, 44, 46
			new byte[8] { 12, 14, 19, 23, 35, 39, 44, 46 },
			// From Field 30: 13, 15, 20, 36, 45, 47
			new byte[6] { 13, 15, 20, 36, 45, 47 },
			// From Field 31: 14, 21, 37, 46
			new byte[4] { 14, 21, 37, 46 },
			// From Field 32: 17, 26, 42, 49
			new byte[4] { 17, 26, 42, 49 },
			// From Field 33: 16, 18, 27, 43, 48, 50
			new byte[6] { 16, 18, 27, 43, 48, 50 },
			// From Field 34: 17, 19, 24, 28, 40, 44, 49, 51
			new byte[8] { 17, 19, 24, 28, 40, 44, 49, 51 },
			// From Field 35: 18, 20, 25, 29, 41, 45, 50, 52
			new byte[8] { 18, 20, 25, 29, 41, 45, 50, 52 },
			// From Field 36: 19, 21, 26, 30, 42, 46, 51, 53
			new byte[8] { 19, 21, 26, 30, 42, 46, 51, 53 },
			// From Field 37: 20, 22, 27, 31, 43, 47, 52, 54
			new byte[8] { 20, 22, 27, 31, 43, 47, 52, 54 },
			// From Field 38: 21, 23, 28, 44, 53, 55
			new byte[6] { 21, 23, 28, 44, 53, 55 },
			// From Field 39: 22, 29, 45, 54
			new byte[4] { 22, 29, 45, 54 },
			// From Field 40: 25, 34, 50, 57
			new byte[4] { 25, 34, 50, 57 },
			// From Field 41: 24, 26, 35, 51, 56, 58
			new byte[6] { 24, 26, 35, 51, 56, 58 },
			// From Field 42: 25, 27, 32, 36, 48, 52, 57, 59
			new byte[8] { 25, 27, 32, 36, 48, 52, 57, 59 },
			// From Field 43: 26, 28, 33, 37, 49, 53, 58, 60
			new byte[8] { 26, 28, 33, 37, 49, 53, 58, 60 },
			// From Field 44: 27, 29, 34, 38, 50, 54, 59, 61
			new byte[8] { 27, 29, 34, 38, 50, 54, 59, 61 },
			// From Field 45: 28, 30, 35, 39, 51, 55, 60, 62
			new byte[8] { 28, 30, 35, 39, 51, 55, 60, 62 },
			// From Field 46: 29, 31, 36, 52, 61, 63
			new byte[6] { 29, 31, 36, 52, 61, 63 },
			// From Field 47: 30, 37, 53, 62
			new byte[4] { 30, 37, 53, 62 },
			// From Field 48: 33, 42, 58
			new byte[3] { 33, 42, 58 },
			// From Field 49: 32, 34, 43, 59
			new byte[4] { 32, 34, 43, 59 },
			// From Field 40: 33, 35, 40, 44, 56, 60
			new byte[6] { 33, 35, 40, 44, 56, 60 },
			// From Field 51: 34, 36, 41, 45, 57, 61
			new byte[6] { 34, 36, 41, 45, 57, 61 },
			// From Field 52: 35, 37, 42, 46, 58, 62
			new byte[6] { 35, 37, 42, 46, 58, 62 },
			// From Field 53: 36, 38, 43, 47, 59, 63
			new byte[6] { 36, 38, 43, 47, 59, 63 },
			// From Field 54: 37, 39, 44, 60
			new byte[4] { 37, 39, 44, 60 },
			// From Field 55: 38, 45, 61
			new byte[3] { 38, 45, 61 },
			// From Field 56: 41, 50
			new byte[2] { 41, 50 },
			// From Field 57: 40, 42, 51
			new byte[3] { 40, 42, 51 },
			// From Field 58: 41, 43, 48, 52
			new byte[4] { 41, 43, 48, 52 },
			// From Field 59: 42, 44, 49, 53
			new byte[4] { 42, 44, 49, 53 },
			// From Field 60: 43, 45, 50, 54
			new byte[4] { 43, 45, 50, 54 },
			// From Field 61: 44, 46, 51, 55
			new byte[4] { 44, 46, 51, 55 },
			// From Field 62: 45, 47, 52
			new byte[3] { 45, 47, 52 },
			// From Field 63: 46, 53
			new byte[2] { 46, 53 }
		};
	}
}