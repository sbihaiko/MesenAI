//ADR-0153 §2 (Phase 9, F9.5) - see SpriteGrouping.h. Stateful partner: HdPackBuilder.
#include "NES/HdPacks/SpriteGrouping.h"
#include "NES/HdPacks/SheetGrouping.h"
#include <algorithm>
#include <cstdlib>
#include <map>
#include <set>
#include <tuple>
#include <utility>

namespace MesenSheets
{
	namespace
	{
		using Offset = std::pair<int32_t, int32_t>;
		using PairKey = std::pair<uint32_t, uint32_t>;

		MetatileKey SpriteKey(ShapeId shape)
		{
			MetatileKey key;
			key.Tiles[0] = shape;
			return key;
		}

		//Cell coordinates for a pixel offset, rounded to the nearest 8x8 cell.
		//The sheet is a legibility surface with a gutter between cells, so an
		//odd-pixel metasprite offset (Excitebike's rider sits a few pixels above
		//the bike, not a whole tile) is worth rounding rather than dropping.
		int32_t ToCells(int32_t pixels)
		{
			return (pixels + (pixels >= 0 ? 4 : -4)) / 8;
		}

		char DirOf(int32_t dx, int32_t dy)
		{
			if(std::abs(dx) >= std::abs(dy)) {
				return dx >= 0 ? 'E' : 'W';
			}
			return dy >= 0 ? 'S' : 'N';
		}

		//Per shape: the number of OAM instances (Appearances) and the number of
		//frames it appeared in at all (NodeFrames, once per frame however many
		//instances - the same quantity ADR-0173 named NodeFrames for floors[]).
		//Per ordered shape pair: how many *frames* held each exact relative
		//offset. Everything here is counted over the de-duplicated frame stream,
		//RepeatCount deliberately ignored, so a paused screen cannot manufacture
		//the minimum count the criterion asks for.
		//
		//ADR-0176 (issue #176): Offsets and NodeFrames are both per frame, so
		//the grouping ratio divides like by like. Appearances stays as the
		//honest instance count - adjacency.json reports it - but is no longer
		//the denominator of that ratio.
		struct SpriteStats
		{
			std::map<uint32_t, uint32_t> Appearances;
			std::map<uint32_t, uint32_t> NodeFrames;
			std::map<PairKey, std::map<Offset, uint32_t>> Offsets;
		};

		SpriteStats Accumulate(const std::vector<OamFrame>& frames, const Vocabulary& vocab)
		{
			SpriteStats stats;
			//Reused across frames to keep the per-frame de-duplication cheap.
			std::set<uint32_t> present;
			std::set<std::pair<PairKey, Offset>> seenThisFrame;
			for(const OamFrame& frame : frames) {
				std::vector<int32_t> cells;
				cells.reserve(frame.Entries.size());
				for(const OamEntry& entry : frame.Entries) {
					cells.push_back(vocab.Find(SpriteKey(entry.Shape)));
				}
				present.clear();
				seenThisFrame.clear();
				for(size_t i = 0; i < cells.size(); i++) {
					if(cells[i] < 0) {
						continue;
					}
					stats.Appearances[(uint32_t)cells[i]]++;
					present.insert((uint32_t)cells[i]);
					for(size_t j = 0; j < cells.size(); j++) {
						//A single vocabulary cell cannot hold two positions in a
						//group, so a shape paired with itself is not an edge.
						if(i == j || cells[j] < 0 || cells[i] == cells[j]) {
							continue;
						}
						int32_t dx = (int32_t)frame.Entries[j].X - (int32_t)frame.Entries[i].X;
						int32_t dy = (int32_t)frame.Entries[j].Y - (int32_t)frame.Entries[i].Y;
						if(std::abs(dx) > kSpriteMaxOffset || std::abs(dy) > kSpriteMaxOffset) {
							continue;
						}
						//ADR-0176 §2: the frame counts once when *some* instance
						//of A has *some* instance of B at this offset. A second
						//instance pair at the same offset in the same frame is
						//the same evidence seen twice, not twice the evidence.
						seenThisFrame.insert(std::make_pair(PairKey((uint32_t)cells[i], (uint32_t)cells[j]), Offset(dx, dy)));
					}
				}
				for(uint32_t node : present) {
					stats.NodeFrames[node]++;
				}
				for(const std::pair<PairKey, Offset>& seen : seenThisFrame) {
					stats.Offsets[seen.first][seen.second]++;
				}
			}
			return stats;
		}

		//The offset the pair holds most often; ties go to the smallest (dx, dy),
		//which the ordered map already hands out first.
		std::pair<Offset, uint32_t> DominantOffset(const std::map<Offset, uint32_t>& offsets)
		{
			std::pair<Offset, uint32_t> best(Offset(0, 0), 0);
			for(const auto& entry : offsets) {
				if(entry.second > best.second) {
					best = entry;
				}
			}
			return best;
		}

		//ADR-0177 (issue #179): a cluster's tiles touched; that is not the same
		//statement as "these tiles are one figure". Whenever one actor walks
		//over or past another, the BuildPoses DSU fuses them and the pack gains
		//an entry holding both. Label those so the composition editor can stop
		//offering them - without deleting anything, exactly as ADR-0173 does
		//for a screen-fixed sprite.
		//
		//The evidence is structural and needs no threshold: an entry is a
		//fusion when its tiles split, at some translation, into two entries the
		//recorder *also* saw standing on their own. Both parts are kept poses,
		//so both already cleared kPoseMinFrames.
		//
		//ADR-0228 (issue #401) closes the gap between that rule and ADR-0179
		//§4's variant: a kept pose plus a remainder of kPoseMinTiles or more
		//tiles is a fusion even when the remainder never stood alone (Bill's
		//death tumble, only ever drawn over the soldier that killed him). It
		//is labelled with the one kept part; a two-part split still wins.
		//
		//ADR-0228 (issue #504) adds the one way "never stood alone" can be the
		//screen's doing rather than the game's: a part drawn only while its own
		//figure was cut by a screen edge was never seen as a figure either. The
		//NES draws no entry the edge cut, so the frame shows the part and nothing
		//else - the same shape as Bill's tumble, and no more evidence than it.
		//SMB3's Piranha Plant is the case: half of it is the whole file's
		//`pose017`, seen in 17 frames at x=248..255, every one of them with the
		//plant's other half starting at x=256.

		//The visible screen in pixels, and in the 8 px cells a pose lives on.
		constexpr int32_t kPoseScreenPixelsX = 256;
		constexpr int32_t kPoseScreenPixelsY = 240;
		constexpr int32_t kPoseScreenCellsX = kPoseScreenPixelsX / 8;
		constexpr int32_t kPoseScreenCellsY = kPoseScreenPixelsY / 8;

		//Where each silhouette sat on screen, in pixels, over the recording. The
		//pose file states a silhouette's tiles and never where it was drawn, so a
		//reader that wants to ask about the screen edge has to bring this with it.
		using PoseOrigins = std::map<std::vector<PoseTile>, std::set<std::pair<int32_t, int32_t>>>;

		//ADR-0228 (issue #504): was `part` only ever drawn with its own figure cut
		//by a screen edge? `rest` is the whole's other tiles at this split's
		//placement (`shift`), and one occurrence is enough to answer no.
		//
		//Two facts have to hold at *every* frame the part appeared in. Its own
		//bounds must reach or pass a screen edge - a part drawn clear of all four
		//edges was seen standing on its own, whatever the rest of the whole was
		//doing. And at least one of the rest's tiles must land past that same edge,
		//so that what the frame could not show is what the split calls the whole's
		//other part. A rest tile that would have been visible and was not drawn is
		//real evidence and keeps the label.
		bool PartOnlyEverSeenClippedByTheScreenEdge(const std::vector<PoseTile>& part, const std::vector<PoseTile>& rest, int32_t shiftX, int32_t shiftY, const PoseOrigins& origins)
		{
			PoseOrigins::const_iterator seen = origins.find(part);
			if(seen == origins.end() || seen->second.empty()) {
				return false;
			}
			//A pose's tiles are normalised to its own top-left, so the part's
			//extent is its largest tile offset plus one tile.
			int32_t partWidth = 0;
			int32_t partHeight = 0;
			for(const PoseTile& member : part) {
				partWidth = std::max(partWidth, member.Px + 8);
				partHeight = std::max(partHeight, member.Py + 8);
			}
			for(const std::pair<int32_t, int32_t>& origin : seen->second) {
				bool left = origin.first <= 0;
				bool right = origin.first + partWidth >= kPoseScreenPixelsX;
				bool top = origin.second <= 0;
				bool bottom = origin.second + partHeight >= kPoseScreenPixelsY;
				if(!left && !right && !top && !bottom) {
					return false;
				}
				//The whole's own cell origin here is the part's less the shift.
				int32_t wholeX = ToCells(origin.first) - shiftX;
				int32_t wholeY = ToCells(origin.second) - shiftY;
				bool cut = false;
				for(const PoseTile& member : rest) {
					int32_t x = wholeX + member.Dx;
					int32_t y = wholeY + member.Dy;
					if((left && x < 0) || (right && x >= kPoseScreenCellsX)
						|| (top && y < 0) || (bottom && y >= kPoseScreenCellsY)) {
						cut = true;
						break;
					}
				}
				if(!cut) {
					return false;
				}
			}
			return true;
		}

		void LabelPoseFusions(std::vector<PoseEntry>& entries, const PoseOrigins& origins)
		{
			//Tile set -> rank. A kept pose's Tiles are normalised (the smallest
			//Dx and the smallest Dy are both 0) and sorted, so the vector is
			//already a usable key. emplace keeps the first, i.e. the best rank,
			//should two entries ever share a set.
			std::map<std::vector<PoseTile>, uint32_t> byTiles;
			//Candidates indexed on their anchor tile's node. Tiles[0] is the
			//topmost-leftmost tile (the sort is Dy, Dx, Node), and a candidate
			//can only align inside another pose at a tile carrying that node -
			//which is what keeps this pass from being poses-squared in practice.
			std::map<uint32_t, std::vector<uint32_t>> byAnchorNode;
			for(size_t i = 0; i < entries.size(); i++) {
				if(entries[i].Tiles.empty()) {
					continue;
				}
				byTiles.emplace(entries[i].Tiles, (uint32_t)i);
				byAnchorNode[entries[i].Tiles[0].Node].push_back((uint32_t)i);
			}

			for(size_t bi = 0; bi < entries.size(); bi++) {
				const std::vector<PoseTile>& whole = entries[bi].Tiles;
				//Both halves have to clear kPoseMinTiles to be poses at all.
				if(whole.size() < 2 * (size_t)kPoseMinTiles) {
					continue;
				}
				std::set<PoseTile> wholeSet(whole.begin(), whole.end());

				//Ranks, ascending: ADR-0177 §2 tries candidates in file order,
				//so the part this names first is the most-seen part the split
				//admits.
				std::set<uint32_t> candidates;
				for(const PoseTile& tile : whole) {
					std::map<uint32_t, std::vector<uint32_t>>::const_iterator bucket = byAnchorNode.find(tile.Node);
					if(bucket == byAnchorNode.end()) {
						continue;
					}
					for(uint32_t rank : bucket->second) {
						if(rank != (uint32_t)bi && entries[rank].Tiles.size() < whole.size()) {
							candidates.insert(rank);
						}
					}
				}

				bool labelled = false;
				//ADR-0228: the first kept part (file order) that fits with a
				//pose-sized remainder, used only when no two-part split exists.
				int64_t onePart = -1;
				for(uint32_t rank : candidates) {
					const std::vector<PoseTile>& part = entries[rank].Tiles;
					const PoseTile& head = part[0];
					for(const PoseTile& tile : whole) {
						if(tile.Node != head.Node) {
							continue;
						}
						int32_t shiftX = tile.Dx - head.Dx;
						int32_t shiftY = tile.Dy - head.Dy;
						std::set<PoseTile> placed;
						bool fits = true;
						for(const PoseTile& member : part) {
							PoseTile moved;
							moved.Node = member.Node;
							moved.Dx = member.Dx + shiftX;
							moved.Dy = member.Dy + shiftY;
							if(!wholeSet.count(moved)) {
								fits = false;
								break;
							}
							placed.insert(moved);
						}
						if(!fits) {
							continue;
						}

						//The remainder, re-normalised to its own top-left -
						//the space every kept pose's Tiles already live in.
						std::vector<PoseTile> rest;
						for(const PoseTile& member : whole) {
							if(!placed.count(member)) {
								rest.push_back(member);
							}
						}
						if(rest.size() < (size_t)kPoseMinTiles) {
							continue;
						}
						//ADR-0228 (issue #504): a part that was only ever drawn
						//with the rest of the figure past a screen edge never
						//stood alone, so this split is the edge's doing and not
						//two figures touching. Skip the candidate; another one
						//may still carry the entry.
						if(PartOnlyEverSeenClippedByTheScreenEdge(part, rest, shiftX, shiftY, origins)) {
							continue;
						}
						int32_t restX = rest[0].Dx;
						int32_t restY = rest[0].Dy;
						for(const PoseTile& member : rest) {
							restX = std::min(restX, member.Dx);
							restY = std::min(restY, member.Dy);
						}
						for(PoseTile& member : rest) {
							member.Dx -= restX;
							member.Dy -= restY;
						}
						std::sort(rest.begin(), rest.end());

						std::map<std::vector<PoseTile>, uint32_t>::const_iterator match = byTiles.find(rest);
						if(match == byTiles.end()) {
							if(onePart < 0) {
								onePart = rank;
							}
							continue;
						}
						entries[bi].FusionOf.push_back(rank);
						entries[bi].FusionOf.push_back(match->second);
						labelled = true;
						break;
					}
					if(labelled) {
						break;
					}
				}
				if(!labelled && onePart >= 0) {
					entries[bi].FusionOf.push_back((uint32_t)onePart);
				}
			}
		}

		//ADR-0225 §1: renumber Z to 0..n-1 in OAM order after de-duplication
		//dropped members, so the written rank has no holes.
		void RankPoseTiles(std::vector<PoseTile>& tiles)
		{
			std::vector<size_t> order(tiles.size());
			for(size_t i = 0; i < order.size(); i++) {
				order[i] = i;
			}
			std::sort(order.begin(), order.end(), [&tiles](size_t a, size_t b) { return tiles[a].Z < tiles[b].Z; });
			for(size_t rank = 0; rank < order.size(); rank++) {
				tiles[order[rank]].Z = (int32_t)rank;
			}
		}

		//ADR-0225 §1: do any two 8x8 tiles of a pixel layout share a pixel?
		bool PoseTilesOverlap(const std::vector<PoseTile>& tiles)
		{
			for(size_t i = 0; i < tiles.size(); i++) {
				for(size_t j = i + 1; j < tiles.size(); j++) {
					if(std::abs(tiles[i].Px - tiles[j].Px) < 8 && std::abs(tiles[i].Py - tiles[j].Py) < 8) {
						return true;
					}
				}
			}
			return false;
		}

		//---- ADR-0179 (F9.20) ---------------------------------------------

		//One spatially connected cluster of a frame: its normalised tile set
		//(the pose identity) and where its top-left sat on screen, in pixels.
		struct PoseCluster
		{
			std::vector<PoseTile> Tiles;
			//ADR-0234: whether the entry that supplied each tile drew a pixel
			//of it in this frame - parallel to Tiles, so an appearance the
			//background hid is not lost before BuildPoses can weigh it.
			std::vector<uint8_t> Visible;
			//#520: the cells of the cluster that hold no art, in cell
			//coordinates relative to the cluster's top-left. They are members of
			//the figure for ADR-0170 §2's floor, but they are not tiles, so they
			//stay out of Tiles and out of the pose. Carried out of SegmentFrame
			//because ADR-0234 re-applies that floor to the reduced pose, and the
			//unit of the floor is cells: a two-tile figure whose other two cells
			//are blank halves is a figure, and a floor counted in tiles would
			//throw it away again after the mask reduction.
			std::set<std::pair<int32_t, int32_t>> ArtlessCells;
			int32_t X = 0;
			int32_t Y = 0;
		};

		//ADR-0234 (issue #505): per cluster identity, whether each of its tiles
		//was drawn visibly in at least one of the frames that cluster was seen
		//in. A tile that never was is what the background hid for the whole
		//life of that pose, and only then does it leave the pose.
		using PoseVisibility = std::map<std::vector<PoseTile>, std::vector<uint8_t>>;

		//The pose a cluster contributes to the recording: its tiles minus the
		//ones this map says never showed. The same reduction is applied by the
		//pose pass and by the track linker, so a pose and its cycles agree.
		std::vector<PoseTile> VisiblePoseTiles(const std::vector<PoseTile>& tiles, const PoseVisibility& visible)
		{
			PoseVisibility::const_iterator it = visible.find(tiles);
			if(it == visible.end()) {
				return tiles;
			}
			std::vector<PoseTile> out;
			for(size_t i = 0; i < tiles.size(); i++) {
				if(i >= it->second.size() || it->second[i]) {
					out.push_back(tiles[i]);
				}
			}
			return out;
		}

		//ADR-0170 §2's floor, in the unit #520 re-based it on: the distinct cells
		//a figure holds, drawn and artless alike. ADR-0234 applies it a second
		//time to the reduced pose, so the count has to be taken the same way
		//there - counting only the tiles left after a mask is removed would put a
		//figure with blank halves back under the floor. Measured on Bubble
		//Bobble's attract recording: 41 of its 67 poses hold fewer than
		//kPoseMinTiles drawn tiles and are figures only because their blank
		//halves are cells, so a tile-count floor here drops most of a game's
		//poses rather than the one tile the mask rule removed.
		size_t PoseCellCount(const std::vector<PoseTile>& tiles, const std::set<std::pair<int32_t, int32_t>>& artlessCells)
		{
			std::set<std::pair<int32_t, int32_t>> cells = artlessCells;
			for(const PoseTile& tile : tiles) {
				cells.insert(std::make_pair(tile.Dx, tile.Dy));
			}
			return cells.size();
		}

		//The ADR-0170 §1 segmentation of one retained frame: entries the
		//vocabulary knows, DSU-joined within kPoseMaxGap on both axes, each
		//cluster normalised to its own top-left at round-to-nearest cell and
		//reduced to a set. Clusters under kPoseMinTiles are not returned.
		//
		//#520: an artless placement (Shape == kEmptyCell) takes part in both -
		//it is a cell of the cluster, so it can carry the figure over the floor,
		//and it is where two OAM entries of one sprite overlap - but it holds no
		//tile, so it never appears in the pose or in the file.
		//
		//ADR-0234: every entry is segmented, and each tile carries whether its
		//entry showed a pixel. Dropping a hidden entry here would cut the
		//figure it belongs to into a variant per occlusion (Punch-Out!!'s
		//second E2E: 34 poses became 101), so the exclusion happens once the
		//cluster's whole life is known - see VisiblePoseTiles.
		std::vector<PoseCluster> SegmentFrame(const OamFrame& frame, const Vocabulary& vocab)
		{
			std::vector<PoseCluster> out;
			//An unknown shape is skipped rather than clustered: it would move
			//the top-left and so shift every offset in the pose.
			//
			//ADR-0234: one segmented appearance is one record, so the drawing
			//fact below can never drift out of step with the position it
			//describes. #520 made the placements a second source of appearances
			//here - they carry no vocabulary node (`Node < 0`) and no drawing
			//facts at all - and such a placement fills one of these like any
			//other, never a vector of its own: an appearance nothing is known
			//about is not a mask (IsMaskEntry needs `hiddenPixels > 0`), so it
			//stays Shown and keeps counting toward the pose floor.
			struct Segmented
			{
				int32_t X = 0;
				int32_t Y = 0;
				//-1 for a placement: a cell of the figure with no tile of its own
				//(#520). It carries the cluster over the floor and is skipped
				//everywhere a tile is wanted.
				int32_t Node = 0;
				//0 takes this appearance out of the pose it belongs to: a mask
				//the background hid for the whole life of that pose. A placement
				//never is one - IsMaskEntry needs `hiddenPixels > 0` - and the
				//default says so.
				uint8_t Shown = 1;
			};
			std::vector<Segmented> entries;
			for(const OamEntry& entry : frame.Entries) {
				int32_t node = vocab.Find(SpriteKey(entry.Shape));
				if(node < 0) {
					//#520: kEmptyCell is a half the PPU placed whose art is
					//blank (#470). It names no node - there is no art to name -
					//but it is one of the cells of the figure, so it joins with
					//node -1 and holds its cell without holding a tile.
					if(entry.Shape != kEmptyCell) {
						continue;
					}
					node = -1;
				}
				Segmented segmented;
				segmented.X = (int32_t)entry.X;
				segmented.Y = (int32_t)entry.Y;
				segmented.Node = node;
				//ADR-0234: read off this appearance alone. A placement, whose
				//drawing facts are at their defaults, comes out Shown - the
				//clause that keeps it a cell of the figure instead of a mask.
				segmented.Shown = IsMaskEntry(entry.BehindBg, entry.VisiblePixels, entry.HiddenPixels) ? 0 : 1;
				entries.push_back(segmented);
			}
			if(entries.size() < kPoseMinTiles) {
				return out;
			}
			Dsu sets(entries.size());
			for(size_t i = 0; i < entries.size(); i++) {
				for(size_t j = i + 1; j < entries.size(); j++) {
					if(std::abs(entries[i].X - entries[j].X) <= kPoseMaxGap && std::abs(entries[i].Y - entries[j].Y) <= kPoseMaxGap) {
						sets.Union((uint32_t)i, (uint32_t)j);
					}
				}
			}
			std::map<uint32_t, std::vector<size_t>> clusters;
			for(size_t i = 0; i < entries.size(); i++) {
				clusters[sets.Find((uint32_t)i)].push_back(i);
			}
			for(const std::pair<const uint32_t, std::vector<size_t>>& cluster : clusters) {
				if(cluster.second.size() < kPoseMinTiles) {
					continue;
				}
				PoseCluster pc;
				//#520: the origin is the drawn figure's own top-left. A blank
				//half occupies screen space but says nothing about where the
				//silhouette starts, so it never moves the origin - the same
				//figure is the same pose whatever transparent cells sit above
				//or beside it.
				pc.X = 0;
				pc.Y = 0;
				bool anyDrawn = false;
				for(size_t index : cluster.second) {
					if(entries[index].Node < 0) {
						continue;
					}
					if(!anyDrawn) {
						pc.X = entries[index].X;
						pc.Y = entries[index].Y;
						anyDrawn = true;
					} else {
						pc.X = std::min(pc.X, entries[index].X);
						pc.Y = std::min(pc.Y, entries[index].Y);
					}
				}
				if(!anyDrawn) {
					continue; //nothing was drawn here, so there is no silhouette
				}
				pc.Tiles.reserve(cluster.second.size());
				//#520: the cells of the cluster that hold no art. They are
				//members - ADR-0225 §1's two-entries-one-cell rule is about the
				//cell, and two artless cells are two cells - but they are not
				//tiles, so they never enter the pose's tile set or the file.
				std::set<std::pair<int32_t, int32_t>> artlessCells;
				//ADR-0234: parallel to pc.Tiles, so a tile's visibility travels
				//with the member the de-duplication below keeps.
				std::vector<uint8_t> memberShown;
				memberShown.reserve(cluster.second.size());
				for(size_t index : cluster.second) {
					if(entries[index].Node < 0) {
						artlessCells.insert(std::make_pair(ToCells(entries[index].X - pc.X), ToCells(entries[index].Y - pc.Y)));
						continue;
					}
					PoseTile tile;
					tile.Node = (uint32_t)entries[index].Node;
					tile.Px = entries[index].X - pc.X;
					tile.Py = entries[index].Y - pc.Y;
					tile.Dx = ToCells(tile.Px);
					tile.Dy = ToCells(tile.Py);
					//cluster.second is in OAM order, so this is the OAM rank.
					tile.Z = (int32_t)pc.Tiles.size();
					pc.Tiles.push_back(tile);
					memberShown.push_back(entries[index].Shown);
				}
				//A set, not a list: two OAM entries of the same shape rounding
				//onto one cell are one member, exactly as the S10.a ground
				//truth counted them. Stable, so the member kept is the
				//frontmost entry (ADR-0225: its pixels are the ones drawn) -
				//and its visibility travels with it, so a hidden entry that
				//loses the cell to a visible one does not hide the tile.
				std::vector<size_t> order(pc.Tiles.size());
				for(size_t i = 0; i < order.size(); i++) {
					order[i] = i;
				}
				std::stable_sort(order.begin(), order.end(), [&pc](size_t a, size_t b) { return pc.Tiles[a] < pc.Tiles[b]; });
				std::vector<PoseTile> tiles;
				pc.Visible.clear();
				for(size_t i = 0; i < order.size(); i++) {
					size_t index = order[i];
					if(!tiles.empty() && tiles.back() == pc.Tiles[index]) {
						continue;
					}
					tiles.push_back(pc.Tiles[index]);
					pc.Visible.push_back(memberShown[index]);
				}
				pc.Tiles = tiles;
				//ADR-0170 §2 counts the cells the cluster holds, and it was
				//calibrated on a stream where a transparent half was one of
				//them: #470 took those out of the stream and a figure the game
				//draws in four cells became two, under the floor (#520). So the
				//count is the cluster's cells, drawn and artless alike, and it is
				//the *union* of them: pc.Tiles is a set of (Node, Dx, Dy) and
				//holds two entries for a cell two shapes were drawn on (ADR-0225
				//§1), and an artless half on a cell some art already holds adds no
				//cell either. Summing the two sets would count those cells twice
				//and let a three-cell figure pass as a four-cell one.
				pc.ArtlessCells = artlessCells;
				if(PoseCellCount(pc.Tiles, pc.ArtlessCells) < kPoseMinTiles) {
					continue;
				}
				RankPoseTiles(pc.Tiles);
				out.push_back(pc);
			}
			return out;
		}

		//ADR-0234: what the OAM stream says about how each sprite shape was
		//drawn, summed over the retained frames. VisiblePixels is how many
		//pixels of its own the shape put on screen, HiddenPixels how many it
		//lost to an opaque background, BehindBgAppearances counts the
		//appearances that carried the OAM priority bit and MaskAppearances the
		//ones IsMaskEntry hid. The pose pass reads the predicate off each entry
		//and the adjacency label reads these sums, so the two cannot disagree
		//about what a mask is.
		struct NodeVisibility
		{
			std::vector<uint32_t> VisiblePixels;
			std::vector<uint32_t> HiddenPixels;
			std::vector<uint32_t> BehindBgAppearances;
			std::vector<uint32_t> MaskAppearances;
		};

		NodeVisibility AccumulateNodeVisibility(const std::vector<OamFrame>& frames, const Vocabulary& vocab)
		{
			NodeVisibility vis;
			vis.VisiblePixels.assign(vocab.Entries.size(), 0);
			vis.HiddenPixels.assign(vocab.Entries.size(), 0);
			vis.BehindBgAppearances.assign(vocab.Entries.size(), 0);
			vis.MaskAppearances.assign(vocab.Entries.size(), 0);
			for(const OamFrame& frame : frames) {
				for(const OamEntry& entry : frame.Entries) {
					int32_t node = vocab.Find(SpriteKey(entry.Shape));
					if(node < 0) {
						continue;
					}
					size_t i = (size_t)node;
					vis.VisiblePixels[i] += entry.VisiblePixels;
					vis.HiddenPixels[i] += entry.HiddenPixels;
					if(entry.BehindBg) {
						vis.BehindBgAppearances[i]++;
					}
					if(IsMaskEntry(entry.BehindBg, entry.VisiblePixels, entry.HiddenPixels)) {
						vis.MaskAppearances[i]++;
					}
				}
			}
			return vis;
		}

		//Does `part`, translated so that its head lands on `at` (a tile of
		//`whole` carrying the head's node), fit inside `wholeSet`? Fills
		//`placed` with the translated members when it does.
		bool PartFitsAt(const std::vector<PoseTile>& part, const PoseTile& at, const std::set<PoseTile>& wholeSet, std::set<PoseTile>& placed)
		{
			const PoseTile& head = part[0];
			int32_t shiftX = at.Dx - head.Dx;
			int32_t shiftY = at.Dy - head.Dy;
			placed.clear();
			for(const PoseTile& member : part) {
				PoseTile moved;
				moved.Node = member.Node;
				moved.Dx = member.Dx + shiftX;
				moved.Dy = member.Dy + shiftY;
				if(!wholeSet.count(moved)) {
					return false;
				}
				placed.insert(moved);
			}
			return true;
		}

		//ADR-0179 §4: a kept, non-fusion pose is a *variant* of the first kept
		//pose (file order) that fits inside it leaving a remainder below
		//kPoseMinTiles - a figure plus its projectile or muzzle flash. The
		//remainder could never be a pose, which is exactly what separates a
		//variant from ADR-0177's fusion. Containment only, no threshold.
		void LabelPoseVariants(std::vector<PoseEntry>& entries)
		{
			std::map<uint32_t, std::vector<uint32_t>> byAnchorNode;
			for(size_t i = 0; i < entries.size(); i++) {
				if(!entries[i].Tiles.empty() && entries[i].FusionOf.empty()) {
					byAnchorNode[entries[i].Tiles[0].Node].push_back((uint32_t)i);
				}
			}
			for(size_t vi = 0; vi < entries.size(); vi++) {
				const std::vector<PoseTile>& whole = entries[vi].Tiles;
				if(!entries[vi].FusionOf.empty() || whole.size() <= (size_t)kPoseMinTiles) {
					continue;
				}
				std::set<PoseTile> wholeSet(whole.begin(), whole.end());
				std::set<uint32_t> candidates;
				for(const PoseTile& tile : whole) {
					std::map<uint32_t, std::vector<uint32_t>>::const_iterator bucket = byAnchorNode.find(tile.Node);
					if(bucket == byAnchorNode.end()) {
						continue;
					}
					for(uint32_t rank : bucket->second) {
						size_t partSize = entries[rank].Tiles.size();
						if(rank != (uint32_t)vi && partSize < whole.size() && whole.size() - partSize < (size_t)kPoseMinTiles) {
							candidates.insert(rank);
						}
					}
				}
				for(uint32_t rank : candidates) {
					const std::vector<PoseTile>& part = entries[rank].Tiles;
					bool fits = false;
					std::set<PoseTile> placed;
					for(const PoseTile& tile : whole) {
						if(tile.Node == part[0].Node && PartFitsAt(part, tile, wholeSet, placed)) {
							fits = true;
							break;
						}
					}
					if(fits) {
						entries[vi].VariantOf = (int32_t)rank;
						break;
					}
				}
			}
		}

		//One run of one pose on a track: the pose and how many frames
		//(RepeatCount included) it was held before the track changed pose.
		typedef PoseTrackRun TrackRun;

		//ADR-0179 §1: greedy nearest-first linking of kept clusters between
		//consecutive retained frames, within kPoseTrackMaxMove. Fills
		//Hold/Next on the entries and returns the tracks as runs.
		//ADR-0226: a cluster left without a partner stays a pending end for up
		//to kPoseTrackMaxGap retained frames (each skipped frame carrying
		//RepeatCount <= kPoseTrackGapMaxRepeats). Each frame links in passes,
		//nearest-first within one pass: the previous frame's clusters first,
		//then the pending ends by increasing age, against what is still
		//unlinked. A bridged link adds the skipped frames' RepeatCount to the
		//run it interrupts, so a run's Held keeps the game's cadence.
		std::vector<std::vector<TrackRun>> LinkPoseTracks(const std::vector<OamFrame>& frames, const Vocabulary& vocab, std::vector<PoseEntry>& entries, const PoseVisibility& visible)
		{
			std::map<std::vector<PoseTile>, uint32_t> rankOf;
			for(size_t i = 0; i < entries.size(); i++) {
				rankOf.emplace(entries[i].Tiles, (uint32_t)i);
			}
			struct Live
			{
				int32_t X;
				int32_t Y;
				uint32_t Pose;
				size_t Track;
				//RepeatCount of the retained frames skipped since this cluster
				//was seen (0 for the previous frame's clusters).
				uint32_t Gap;
			};
			std::vector<std::vector<TrackRun>> tracks;
			//ends[0]: the previous frame's clusters; ends[k]: clusters of the
			//frame k+1 back that are still unlinked (pending, ADR-0226).
			std::vector<std::vector<Live>> ends;
			for(const OamFrame& frame : frames) {
				std::vector<Live> cur;
				for(const PoseCluster& cluster : SegmentFrame(frame, vocab)) {
					//ADR-0234: the same reduction BuildPoses keyed the poses
					//with, and the same floor in the same unit, so a frame links
					//to the pose it helped define.
					std::vector<PoseTile> tiles = VisiblePoseTiles(cluster.Tiles, visible);
					if(PoseCellCount(tiles, cluster.ArtlessCells) < kPoseMinTiles) {
						continue;
					}
					std::map<std::vector<PoseTile>, uint32_t>::const_iterator it = rankOf.find(tiles);
					if(it == rankOf.end()) {
						continue; //below the ADR-0170 §2 thresholds: invisible to the linker
					}
					Live live;
					live.X = cluster.X;
					live.Y = cluster.Y;
					live.Pose = it->second;
					live.Track = (size_t)-1;
					live.Gap = 0;
					cur.push_back(live);
				}
				std::vector<std::vector<bool>> endUsed(ends.size());
				for(size_t age = 0; age < ends.size(); age++) {
					std::vector<Live>& prev = ends[age];
					endUsed[age].assign(prev.size(), false);
					//Every candidate link, nearest first; ties broken by position in
					//either frame so two saves of one stream link identically.
					std::vector<std::tuple<int32_t, size_t, size_t>> links;
					for(size_t pi = 0; pi < prev.size(); pi++) {
						for(size_t ci = 0; ci < cur.size(); ci++) {
							if(cur[ci].Track != (size_t)-1) {
								continue; //claimed by an earlier (younger) pass
							}
							int32_t d = std::abs(prev[pi].X - cur[ci].X) + std::abs(prev[pi].Y - cur[ci].Y);
							if(d <= kPoseTrackMaxMove) {
								links.push_back(std::make_tuple(d, pi, ci));
							}
						}
					}
					std::sort(links.begin(), links.end());
					for(const std::tuple<int32_t, size_t, size_t>& link : links) {
						size_t pi = std::get<1>(link);
						size_t ci = std::get<2>(link);
						if(endUsed[age][pi] || cur[ci].Track != (size_t)-1) {
							continue;
						}
						endUsed[age][pi] = true;
						cur[ci].Track = prev[pi].Track;
						std::vector<TrackRun>& track = tracks[prev[pi].Track];
						//ADR-0226 §1: the skipped frames belong to the run they interrupt.
						track.back().Held += prev[pi].Gap;
						if(prev[pi].Pose == cur[ci].Pose) {
							entries[cur[ci].Pose].Hold++;
							track.back().Held += frame.RepeatCount;
						} else {
							std::vector<PoseLink>& next = entries[prev[pi].Pose].Next;
							bool found = false;
							for(PoseLink& edge : next) {
								if(edge.Pose == cur[ci].Pose) {
									edge.Count++;
									found = true;
									break;
								}
							}
							if(!found) {
								PoseLink edge;
								edge.Pose = cur[ci].Pose;
								edge.Count = 1;
								next.push_back(edge);
							}
							TrackRun run;
							run.Frame = frame.FrameNumber;
							run.Pose = cur[ci].Pose;
							run.Held = frame.RepeatCount;
							track.push_back(run);
						}
					}
				}
				for(Live& live : cur) {
					if(live.Track == (size_t)-1) {
						TrackRun run;
						run.Frame = frame.FrameNumber;
						run.Pose = live.Pose;
						run.Held = frame.RepeatCount;
						tracks.push_back(std::vector<TrackRun>(1, run));
						live.Track = tracks.size() - 1;
					}
				}
				//Unlinked ends age by one frame - this one, now skipped - unless
				//it was held too long to be flicker; the rest end their tracks.
				std::vector<std::vector<Live>> aged(1, cur);
				if(frame.RepeatCount <= kPoseTrackGapMaxRepeats) {
					for(size_t age = 0; age < ends.size() && age < (size_t)kPoseTrackMaxGap; age++) {
						std::vector<Live> still;
						for(size_t pi = 0; pi < ends[age].size(); pi++) {
							if(!endUsed[age][pi]) {
								Live live = ends[age][pi];
								live.Gap += frame.RepeatCount;
								still.push_back(live);
							}
						}
						aged.push_back(still);
					}
				}
				ends.swap(aged);
			}
			for(PoseEntry& entry : entries) {
				std::stable_sort(entry.Next.begin(), entry.Next.end(), [](const PoseLink& a, const PoseLink& b) {
					if(a.Count != b.Count) { return a.Count > b.Count; }
					return a.Pose < b.Pose;
				});
			}
			return tracks;
		}

		uint32_t MedianOf(std::vector<uint32_t> values)
		{
			if(values.empty()) {
				return 0;
			}
			std::sort(values.begin(), values.end());
			return values[values.size() / 2];
		}

		//A cycle occurrence, canonically rotated: the most-seen pose (lowest
		//rank) first; when it occurs twice in the period, the rotation with
		//the lexicographically smallest pose vector.
		std::vector<uint32_t> CanonicalRotation(const std::vector<uint32_t>& block, size_t& shift)
		{
			std::vector<uint32_t> best;
			shift = 0;
			for(size_t r = 0; r < block.size(); r++) {
				if(block[r] != *std::min_element(block.begin(), block.end())) {
					continue;
				}
				std::vector<uint32_t> rotated;
				for(size_t k = 0; k < block.size(); k++) {
					rotated.push_back(block[(r + k) % block.size()]);
				}
				if(best.empty() || rotated < best) {
					best = rotated;
					shift = r;
				}
			}
			return best;
		}

		//ADR-0179 §3: cycles by period repetition on each track, then
		//sequences by identical occurrence on what the cycles left uncovered.
		void FindPoseRuns(const std::vector<std::vector<TrackRun>>& tracks, const std::vector<OamFrame>& frames, PoseStats& stats)
		{
			struct Occurrences
			{
				uint32_t Repeats = 0;
				std::vector<std::vector<uint32_t>> Holds; //per position
				//ADR-0181 §3: one entry per window - the emulated frame it began
				//on, the one its last phase advance began on, and that phase's
				//canonical position, so the stop frame can be read once the
				//median holds are known.
				struct Window
				{
					uint32_t Start = 0;
					uint32_t LastAdvance = 0;
					size_t Phase = 0;
				};
				std::vector<Window> Windows;
			};
			//Emulated frame each retained frame begins on (RepeatCount summed),
			//and the frames on which a port released some button - the byte lost
			//a bit against the previous retained frame (ADR-0181 §1).
			std::vector<uint32_t> frameStart(frames.size() + 1, 0);
			for(size_t i = 0; i < frames.size(); i++) {
				frameStart[i + 1] = frameStart[i] + std::max<uint32_t>(1, frames[i].RepeatCount);
			}
			std::vector<uint32_t> releases[2];
			for(size_t i = 1; i < frames.size(); i++) {
				for(size_t port = 0; port < 2; port++) {
					if(frames[i - 1].Buttons[port] & ~frames[i].Buttons[port]) {
						releases[port].push_back(frameStart[i]);
					}
				}
			}
			std::map<std::vector<uint32_t>, Occurrences> cycles;
			std::map<std::vector<uint32_t>, Occurrences> sequences;

			for(const std::vector<TrackRun>& track : tracks) {
				size_t n = track.size();
				std::vector<bool> covered(n, false);
				size_t start = 0;
				while(start < n) {
					bool found = false;
					for(size_t p = 2; p <= (size_t)kPoseCycleMaxPeriod && start + 2 * p <= n; p++) {
						size_t r = 1;
						while(start + (r + 1) * p <= n) {
							bool same = true;
							for(size_t k = 0; k < p && same; k++) {
								same = track[start + k].Pose == track[start + r * p + k].Pose;
							}
							if(!same) {
								break;
							}
							r++;
						}
						if(r < (size_t)kPoseCycleMinRepeats) {
							continue;
						}
						std::vector<uint32_t> block;
						for(size_t k = 0; k < p; k++) {
							block.push_back(track[start + k].Pose);
						}
						if(std::set<uint32_t>(block.begin(), block.end()).size() < 2) {
							continue;
						}
						size_t shift = 0;
						std::vector<uint32_t> key = CanonicalRotation(block, shift);
						Occurrences& occ = cycles[key];
						occ.Repeats += (uint32_t)r;
						occ.Holds.resize(p);
						for(size_t k = 0; k < r * p; k++) {
							occ.Holds[(k + p - shift) % p].push_back(track[start + k].Held);
						}
						//Cover the full repeats and the partial repeat that
						//follows, so a tail of the loop is not read as a sequence.
						size_t end = start + r * p;
						while(end < n && track[end].Pose == block[(end - start) % p]) {
							end++;
						}
						Occurrences::Window window;
						window.Start = frameStart[std::min<size_t>(track[start].Frame, frames.size())];
						window.LastAdvance = frameStart[std::min<size_t>(track[end - 1].Frame, frames.size())];
						window.Phase = (end - 1 - start + p - shift) % p;
						occ.Windows.push_back(window);
						for(size_t k = start; k < end; k++) {
							covered[k] = true;
						}
						start = end;
						found = true;
						break;
					}
					if(!found) {
						start++;
					}
				}
				//Uncovered segments -> every all-distinct window of an allowed length.
				size_t i = 0;
				while(i < n) {
					if(covered[i]) {
						i++;
						continue;
					}
					size_t j = i;
					while(j < n && !covered[j]) {
						j++;
					}
					for(size_t a = i; a < j; a++) {
						std::set<uint32_t> seen;
						std::vector<uint32_t> window;
						std::vector<uint32_t> holds;
						for(size_t b = a; b < j && window.size() < (size_t)kPoseSequenceMaxLength; b++) {
							if(!seen.insert(track[b].Pose).second) {
								break;
							}
							window.push_back(track[b].Pose);
							holds.push_back(track[b].Held);
							if(window.size() >= (size_t)kPoseSequenceMinLength) {
								Occurrences& occ = sequences[window];
								occ.Repeats++;
								occ.Holds.resize(window.size());
								for(size_t k = 0; k < holds.size(); k++) {
									occ.Holds[k].push_back(holds[k]);
								}
							}
						}
					}
					i = j;
				}
			}

			for(const std::pair<const std::vector<uint32_t>, Occurrences>& cycle : cycles) {
				PoseRun run;
				run.Poses = cycle.first;
				run.Repeats = cycle.second.Repeats;
				for(const std::vector<uint32_t>& holds : cycle.second.Holds) {
					run.Hold.push_back(MedianOf(holds));
				}
				//ADR-0181 §3: a window stops when the next advance was due and did
				//not come - last advance + that phase's median hold. It answers a
				//port when a release on it happened while the window was live and
				//within kDriverStopLag frames before the stop; a release before the
				//window began cannot have interrupted it.
				run.Windows = (uint32_t)cycle.second.Windows.size();
				for(const Occurrences::Window& window : cycle.second.Windows) {
					uint32_t stop = window.LastAdvance + run.Hold[window.Phase];
					for(size_t port = 0; port < 2; port++) {
						for(uint32_t release : releases[port]) {
							if(release >= window.Start && release <= stop && stop - release <= kDriverStopLag) {
								run.Stops[port]++;
								break;
							}
						}
					}
				}
				bool passes[2];
				for(size_t port = 0; port < 2; port++) {
					passes[port] = run.Windows >= kDriverMinWindows && run.Stops[port] * kDriverStopShareDen >= run.Windows * kDriverStopShareNum;
				}
				run.Driver = passes[0] && !passes[1] ? 1 : passes[1] && !passes[0] ? 2 : 0;
				stats.Cycles.push_back(run);
			}
			//Longest first, then most repeated: a window inside an accepted
			//longer one is the same animation seen shorter, not a second one.
			std::vector<std::pair<std::vector<uint32_t>, Occurrences>> candidates;
			for(const std::pair<const std::vector<uint32_t>, Occurrences>& seq : sequences) {
				if(seq.second.Repeats >= kPoseCycleMinRepeats) {
					candidates.push_back(seq);
				}
			}
			std::stable_sort(candidates.begin(), candidates.end(), [](const std::pair<std::vector<uint32_t>, Occurrences>& a, const std::pair<std::vector<uint32_t>, Occurrences>& b) {
				if(a.first.size() != b.first.size()) { return a.first.size() > b.first.size(); }
				if(a.second.Repeats != b.second.Repeats) { return a.second.Repeats > b.second.Repeats; }
				return a.first < b.first;
			});
			//A window of a cycle is that cycle seen too briefly to repeat - a
			//soldier who walked one and a half turns before he died. ADR-0179
			//§3 says "not part of any cycle", so it is matched around the loop.
			std::vector<std::vector<uint32_t>> loops;
			for(const PoseRun& cycle : stats.Cycles) {
				std::vector<uint32_t> twice = cycle.Poses;
				twice.insert(twice.end(), cycle.Poses.begin(), cycle.Poses.end());
				loops.push_back(twice);
			}
			std::vector<std::vector<uint32_t>> accepted;
			for(const std::pair<std::vector<uint32_t>, Occurrences>& seq : candidates) {
				bool inside = false;
				for(const std::vector<uint32_t>& loop : loops) {
					if(seq.first.size() <= loop.size() / 2 && std::search(loop.begin(), loop.end(), seq.first.begin(), seq.first.end()) != loop.end()) {
						inside = true;
						break;
					}
				}
				for(const std::vector<uint32_t>& longer : accepted) {
					if(inside) {
						break;
					}
					if(std::search(longer.begin(), longer.end(), seq.first.begin(), seq.first.end()) != longer.end()) {
						inside = true;
						break;
					}
				}
				if(inside) {
					continue;
				}
				accepted.push_back(seq.first);
				PoseRun run;
				run.Poses = seq.first;
				run.Repeats = seq.second.Repeats;
				for(const std::vector<uint32_t>& holds : seq.second.Holds) {
					run.Hold.push_back(MedianOf(holds));
				}
				stats.Sequences.push_back(run);
			}
			auto byRepeats = [](const PoseRun& a, const PoseRun& b) {
				if(a.Repeats != b.Repeats) { return a.Repeats > b.Repeats; }
				return a.Poses < b.Poses;
			};
			std::stable_sort(stats.Cycles.begin(), stats.Cycles.end(), byRepeats);
			std::stable_sort(stats.Sequences.begin(), stats.Sequences.end(), byRepeats);
			stats.Tracks = (uint32_t)tracks.size();
		}
	}

	//ADR-0234 (issue #505): which sprite-vocabulary nodes the game draws as a
	//mask at least once - a tile placed behind the background to hide
	//something in front of it, which put none of its own pixels on screen and
	//so is not part of any figure. This is the label the adjacency sidecar
	//carries with its counts; the pose clusters do not read it, because they
	//drop the mask *appearances* (SegmentFrame's IsMaskEntry) rather than the
	//shape - SMB3 draws the very same 16 pattern bytes in front and fully
	//visible on 11 frames of the intro card, and those appearances are tiles
	//like any other. Nothing is deleted here either: the vocabulary, the
	//sheets and adjacency.json keep the node.
	//
	//A pack recorded before this ADR carries neither fact (BehindBg false
	//everywhere, VisiblePixels 0), so the priority half of the predicate keeps
	//every node of such a stream out of the class and its poses are unchanged.
	std::vector<uint8_t> SelectMaskNodes(const std::vector<OamFrame>& frames, const Vocabulary& vocab)
	{
		NodeVisibility vis = AccumulateNodeVisibility(frames, vocab);
		std::vector<uint8_t> mask(vocab.Entries.size(), 0);
		for(size_t node = 0; node < mask.size(); node++) {
			mask[node] = vis.MaskAppearances[node] > 0 ? 1 : 0;
		}
		return mask;
	}

	Vocabulary BuildSpriteVocabulary(const std::vector<OamFrame>& frames)
	{
		std::map<ShapeId, uint32_t> counts;
		for(const OamFrame& frame : frames) {
			for(const OamEntry& entry : frame.Entries) {
				if(entry.Shape != kEmptyCell) {
					counts[entry.Shape]++;
				}
			}
		}

		Vocabulary vocab;
		//Unit 8: an OAM entry is one 8x8 tile, and the sprite sheet's cell grid
		//is what turns a pixel offset into a cell offset.
		vocab.Grid.Unit = 8;
		for(const auto& entry : counts) {
			MetatileEntry cell;
			cell.Key = SpriteKey(entry.first);
			cell.Count = entry.second;
			vocab.Entries.push_back(cell);
		}
		std::stable_sort(vocab.Entries.begin(), vocab.Entries.end(), [](const MetatileEntry& a, const MetatileEntry& b) {
			return a.Count != b.Count ? a.Count > b.Count : a.Key < b.Key;
		});
		for(uint32_t i = 0; i < vocab.Entries.size(); i++) {
			vocab.Index[vocab.Entries[i].Key] = i;
		}
		return vocab;
	}

	std::vector<GroupEdge> SelectSpriteEdges(const std::vector<OamFrame>& frames, const Vocabulary& vocab, uint32_t minCount, double minProb)
	{
		SpriteStats stats = Accumulate(frames, vocab);
		std::vector<GroupEdge> edges;
		for(const auto& pair : stats.Offsets) {
			//Accumulate() counts both orderings of every pair with mirrored
			//offsets, so one of the two is enough; A < B keeps Dx/Dy reading
			//"where B sits relative to A" and the evidence list free of twins.
			if(pair.first.first >= pair.first.second) {
				continue;
			}
			std::pair<Offset, uint32_t> dominant = DominantOffset(pair.second);
			uint32_t count = dominant.second;
			uint32_t framesA = stats.NodeFrames[pair.first.first];
			uint32_t framesB = stats.NodeFrames[pair.first.second];
			if(count < minCount || framesA == 0 || framesB == 0) {
				continue;
			}
			//The metatile criterion's denominator is "every placement of A in
			//that direction"; the OAM analogue is "every frame A is on screen",
			//so a sprite that is only sometimes at this offset - or that turns
			//up without its partner - fails exactly like sand next to
			//everything.
			//
			//ADR-0176 (issue #176): the denominator used to be Appearances, the
			//instance count, against a numerator that is per frame. A shape
			//drawn twice in one frame then had an arithmetic ceiling of 0.5 and
			//every one of its edges was dropped regardless of the evidence -
			//the same biased denominator ADR-0173 fixed for floors[]. Both
			//sides are per frame now; kSheetMinPairCount / kSheetMinPairProb
			//keep their values and their meaning.
			double probAb = (double)count / framesA;
			double probBa = (double)count / framesB;
			if(probAb < minProb || probBa < minProb) {
				continue;
			}
			GroupEdge edge;
			edge.A = pair.first.first;
			edge.B = pair.first.second;
			edge.Dx = ToCells(dominant.first.first);
			edge.Dy = ToCells(dominant.first.second);
			edge.Dir = DirOf(dominant.first.first, dominant.first.second);
			edge.Count = count;
			edge.ProbAB = probAb;
			edge.ProbBA = probBa;
			edges.push_back(edge);
		}
		return edges;
	}

	std::vector<SheetGroup> BuildSprites(const std::vector<OamFrame>& frames, const Vocabulary& vocab, uint32_t minCount, double minProb)
	{
		return LayoutGroups(vocab, SelectSpriteEdges(frames, vocab, minCount, minProb));
	}

	std::vector<SheetGroup> BuildSprites(const std::vector<OamFrame>& frames, const Vocabulary& vocab)
	{
		return BuildSprites(frames, vocab, kSheetMinPairCount, kSheetMinPairProb);
	}

	//ADR-0164 §1 (F9.17): see SpriteGrouping.h. Where SelectSpriteEdges throws
	//the losing mass away, this keeps it: the pairs that scored 0.3 are what a
	//composition tool re-ranks under a lock, and the denominators it divides by
	//must be on disk with the same meaning they had at grouping time.
	SpriteAdjacencyStats AccumulateSpriteAdjacency(const std::vector<OamFrame>& frames, const Vocabulary& vocab)
	{
		SpriteAdjacencyStats stats;
		stats.OamFrames = (uint32_t)frames.size();
		std::vector<std::map<uint32_t, uint32_t>> floorCounts(vocab.Entries.size());
		std::vector<std::set<uint32_t>> seenPositions(vocab.Entries.size());
		std::vector<uint32_t> nodeFrames(vocab.Entries.size(), 0);
		//Directed within-cap offsets per unordered pair. Only the lower index as
		//reference is recorded (mirroring SelectSpriteEdges, which keeps one of
		//the two mirrored orderings), so Dx/Dy always read "where B sits
		//relative to A".
		std::map<PairKey, std::map<Offset, uint32_t>> offsets;
		std::map<PairKey, uint32_t> coFrames;

		for(const OamFrame& frame : frames) {
			std::vector<int32_t> cells;
			cells.reserve(frame.Entries.size());
			std::set<uint32_t> present;
			for(const OamEntry& entry : frame.Entries) {
				int32_t cell = vocab.Find(SpriteKey(entry.Shape));
				cells.push_back(cell);
				if(cell < 0) {
					continue;
				}
				present.insert((uint32_t)cell);
				//Bottom edge (Y + 8) quantised to 8 px. Accumulated per instance
				//over the de-duplicated frames, like Appearances: an 8x16 figure's
				//lower half lands on the true bottom, and two identical actors on
				//one ground both reach the same band.
				uint32_t band = ((uint32_t)entry.Y + 8) & ~7u;
				floorCounts[(size_t)cell][band]++;
				seenPositions[(size_t)cell].insert(((uint32_t)entry.X << 8) | (uint32_t)entry.Y);
			}
			//Co-presence: both shapes on screen at all, any distance, counted
			//once per frame. Every pair that ever shares a frame and clears the
			//noise floor reaches the file - a boss and a level enemy that never
			//come within 32 px get a pair with empty offsets, not none at all.
			for(std::set<uint32_t>::const_iterator it = present.begin(); it != present.end(); ++it) {
				nodeFrames[*it]++;
				for(std::set<uint32_t>::const_iterator inner = it; inner != present.end(); ++inner) {
					if(inner == it) {
						continue;
					}
					coFrames[PairKey(*it, *inner)]++;
				}
			}
			for(size_t i = 0; i < cells.size(); i++) {
				if(cells[i] < 0) {
					continue;
				}
				for(size_t j = 0; j < cells.size(); j++) {
					if(i == j || cells[j] < 0 || cells[i] >= cells[j]) {
						continue;
					}
					int32_t dx = (int32_t)frame.Entries[j].X - (int32_t)frame.Entries[i].X;
					int32_t dy = (int32_t)frame.Entries[j].Y - (int32_t)frame.Entries[i].Y;
					if(std::abs(dx) > kSpriteMaxOffset || std::abs(dy) > kSpriteMaxOffset) {
						continue;
					}
					offsets[PairKey((uint32_t)cells[i], (uint32_t)cells[j])][Offset(dx, dy)]++;
				}
			}
		}

		//Floors: the top kAdjacencyMaxFloors bands per node, count descending
		//then band ascending, so two saves of one recording never wobble.
		stats.Floors.resize(vocab.Entries.size());
		for(size_t node = 0; node < vocab.Entries.size(); node++) {
			std::vector<SpriteFloorBand> bands;
			for(const std::pair<const uint32_t, uint32_t>& band : floorCounts[node]) {
				SpriteFloorBand sample;
				sample.Bottom = band.first;
				sample.Count = band.second;
				bands.push_back(sample);
			}
			std::sort(bands.begin(), bands.end(), [](const SpriteFloorBand& a, const SpriteFloorBand& b) {
				return a.Count != b.Count ? a.Count > b.Count : a.Bottom < b.Bottom;
			});
			if(bands.size() > kAdjacencyMaxFloors) {
				bands.resize(kAdjacencyMaxFloors);
			}
			stats.Floors[node] = std::move(bands);
		}
		stats.Positions.resize(vocab.Entries.size());
		for(size_t node = 0; node < vocab.Entries.size(); node++) {
			stats.Positions[node] = (uint32_t)seenPositions[node].size();
		}
		stats.NodeFrames = std::move(nodeFrames);
		//ADR-0234: the mask label and the three numbers behind it, from the
		//same accumulation SelectMaskNodes reads.
		NodeVisibility visibility = AccumulateNodeVisibility(frames, vocab);
		stats.BehindBgAppearances = visibility.BehindBgAppearances;
		stats.VisiblePixels = visibility.VisiblePixels;
		stats.HiddenPixels = visibility.HiddenPixels;
		stats.MaskAppearances = visibility.MaskAppearances;
		stats.Mask.resize(vocab.Entries.size());
		for(size_t node = 0; node < vocab.Entries.size(); node++) {
			stats.Mask[node] = visibility.MaskAppearances[node] > 0 ? 1 : 0;
		}
		//ADR-0173: screen furniture - a HUD bar, a menu icon - is drawn at a
		//handful of fixed pixels for the whole capture, so its bottom edge lands
		//in several quantised bands at once and joins every one of them as a
		//member. An actor visits a new position nearly every frame it is on
		//screen; furniture returns to the same one over and over. That ratio is
		//the test, and it needs enough frames to mean anything.
		stats.ScreenFixed.assign(vocab.Entries.size(), 0);
		for(size_t node = 0; node < vocab.Entries.size(); node++) {
			//Neither `frames` (the function's OAM-stream parameter, C4457) nor
			//`nodeFrames` (the vector declared above, C4456): MSVC treats both
			//shadowing warnings as errors, and this scope is inside both.
			uint32_t frameCount = stats.NodeFrames[node];
			uint32_t positions = stats.Positions[node];
			if(frameCount >= kScreenFixedMinFrames && positions > 0 &&
				(uint64_t)positions * kScreenFixedRevisits <= (uint64_t)frameCount) {
				stats.ScreenFixed[node] = 1;
			}
		}

		//Pairs: only those with enough co-presence to be evidence, offsets
		//pruned to the top kAdjacencyMaxOffsets by count (ties by offset).
		for(const std::pair<const PairKey, uint32_t>& pair : coFrames) {
			if(pair.second < kAdjacencyMinPairCount) {
				continue;
			}
			SpritePairStat stat;
			stat.A = pair.first.first;
			stat.B = pair.first.second;
			stat.CoFrames = pair.second;
			std::map<PairKey, std::map<Offset, uint32_t>>::const_iterator match = offsets.find(pair.first);
			if(match != offsets.end()) {
				std::vector<SpriteOffsetSample> samples;
				for(const std::pair<const Offset, uint32_t>& offset : match->second) {
					SpriteOffsetSample sample;
					sample.Dx = offset.first.first;
					sample.Dy = offset.first.second;
					sample.Count = offset.second;
					stat.Count += offset.second;
					samples.push_back(sample);
				}
				std::sort(samples.begin(), samples.end(), [](const SpriteOffsetSample& a, const SpriteOffsetSample& b) {
					if(a.Count != b.Count) { return a.Count > b.Count; }
					if(a.Dx != b.Dx) { return a.Dx < b.Dx; }
					return a.Dy < b.Dy;
				});
				for(size_t i = 0; i < samples.size(); i++) {
					if(i < kAdjacencyMaxOffsets) {
						stat.Offsets.push_back(samples[i]);
					} else {
						stat.Other += samples[i].Count;
					}
				}
			}
			stats.Pairs.push_back(stat);
		}
		return stats;
	}

	//ADR-0170 (F9.19): poses, from the per-frame structure AccumulateSpriteAdjacency
	//throws away. See SpriteGrouping.h for why the pairwise projection cannot
	//answer this question.
	InputStats BuildInputStats(const std::vector<OamFrame>& frames)
	{
		InputStats input;
		bool portSeen[2] = { false, false };
		for(const OamFrame& frame : frames) {
			input.Frames += frame.RepeatCount;
			uint8_t held = 0;
			bool pair[4][2] = {};
			for(uint32_t port = 0; port < 2; port++) {
				uint8_t byte = frame.Buttons[port];
				held |= byte;
				portSeen[port] = portSeen[port] || byte != 0;
				//A direction+action pair is a move one player made, so it is
				//read within a port; port 1 holding Right while port 2 holds A
				//is not a Right+A. Directions are bits 4..7, actions bits 0..1.
				for(uint32_t d = 0; d < 4; d++) {
					for(uint32_t a = 0; a < 2; a++) {
						pair[d][a] = pair[d][a] || ((byte & (1 << (4 + d))) && (byte & (1 << a)));
					}
				}
			}
			for(uint32_t b = 0; b < kButtonCount; b++) {
				if(held & (1 << b)) {
					input.Held[b] += frame.RepeatCount;
				}
			}
			for(uint32_t d = 0; d < 4; d++) {
				for(uint32_t a = 0; a < 2; a++) {
					if(pair[d][a]) {
						input.Pairs[d][a] += frame.RepeatCount;
					}
				}
			}
		}
		input.Ports = (portSeen[0] ? 1 : 0) + (portSeen[1] ? 1 : 0);
		return input;
	}

	PoseStats BuildPoses(const std::vector<OamFrame>& frames, const Vocabulary& vocab)
	{
		PoseStats stats;
		stats.RetainedFrames = (uint32_t)frames.size();
		//Sets-equal identity: the same body with a projectile one cell further
		//away is a different pose. Deliberate - a looser identity can merge two
		//real poses, and that failure is invisible in the file (ADR-0170).
		std::map<std::vector<PoseTile>, uint32_t> seen;
		//ADR-0225 §1: per pose, the frames each pixel layout (the (Px, Py)
		//vector in identity order) was seen in, and the cluster of the earliest
		//retained frame that drew it - one occurrence supplies every tile's
		//pixels and rank. OAM order is not part of the key: the vector wins
		//by frames, then that earliest frame supplies Z.
		std::map<std::vector<PoseTile>, std::map<std::vector<int32_t>, std::pair<uint32_t, std::vector<PoseTile>>>> layouts;
		//ADR-0228 (issue #504): the screen positions each silhouette was seen at,
		//so that LabelPoseFusions can tell a figure standing alone from one the
		//screen edge cut in half. Keyed by the pose's own tiles, i.e. the reduced
		//set below - the same key `seen` and the entries carry, so a fusion label
		//finds the origins of the pose it is labelling.
		PoseOrigins origins;
		//ADR-0234: one pass to learn which tiles each cluster ever drew
		//visibly, then the usual accumulation over the clusters reduced to
		//those tiles. A cluster whose tile was hidden in every frame of its
		//life is the same pose without it, and the frames of both forms add up
		//into one pose - which is what keeps the mask out of the figures
		//without inventing an occlusion variant per frame.
		PoseVisibility visible;
		for(const OamFrame& frame : frames) {
			for(const PoseCluster& cluster : SegmentFrame(frame, vocab)) {
				std::vector<uint8_t>& flags = visible[cluster.Tiles];
				if(flags.size() != cluster.Tiles.size()) {
					flags.assign(cluster.Tiles.size(), 0);
				}
				for(size_t i = 0; i < flags.size() && i < cluster.Visible.size(); i++) {
					flags[i] = (uint8_t)(flags[i] | cluster.Visible[i]);
				}
			}
		}
		for(const OamFrame& frame : frames) {
			stats.Frames += frame.RepeatCount;
			for(const PoseCluster& cluster : SegmentFrame(frame, vocab)) {
				std::vector<PoseTile> tiles = VisiblePoseTiles(cluster.Tiles, visible);
				//ADR-0234 applies ADR-0170 §2's floor again to the reduced pose,
				//in the unit #520 re-based it on - the figure's cells, of which
				//the artless placements are some (see PoseCellCount).
				if(PoseCellCount(tiles, cluster.ArtlessCells) < kPoseMinTiles) {
					continue;
				}
				seen[tiles] += frame.RepeatCount;
				//ADR-0228: the origins belong to the pose the file will carry, so
				//they are keyed by the reduced set - the mask's cell is not part
				//of where this figure stood.
				origins[tiles].insert(std::make_pair(cluster.X, cluster.Y));
				std::vector<int32_t> pixels;
				for(const PoseTile& tile : tiles) {
					pixels.push_back(tile.Px);
					pixels.push_back(tile.Py);
				}
				std::pair<uint32_t, std::vector<PoseTile>>& layout = layouts[tiles][pixels];
				if(layout.first == 0) {
					layout.second = tiles;
				}
				layout.first += frame.RepeatCount;
			}
		}

		stats.PosesFound = (uint32_t)seen.size();
		std::vector<PoseEntry> kept;
		for(const std::pair<const std::vector<PoseTile>, uint32_t>& pose : seen) {
			if(pose.second < kPoseMinFrames) {
				continue;
			}
			PoseEntry entry;
			//Most-seen layout; the ordered map hands the lexicographically
			//smallest vector out first, so a strict > breaks ties toward it.
			uint32_t bestFrames = 0;
			for(const auto& layout : layouts[pose.first]) {
				if(layout.second.first > bestFrames) {
					bestFrames = layout.second.first;
					entry.Tiles = layout.second.second;
				}
			}
			entry.Overlaps = PoseTilesOverlap(entry.Tiles);
			entry.Frames = pose.second;
			for(const PoseTile& tile : entry.Tiles) {
				entry.Width = std::max(entry.Width, (uint32_t)(tile.Dx + 1));
				entry.Height = std::max(entry.Height, (uint32_t)(tile.Dy + 1));
			}
			kept.push_back(entry);
		}
		stats.PosesKept = (uint32_t)kept.size();

		//Frames descending, then by the tile set - the ADR sorts "by frames,
		//then by id", and the id is the position in this order, so the set is
		//what breaks the tie deterministically.
		std::stable_sort(kept.begin(), kept.end(), [](const PoseEntry& a, const PoseEntry& b) {
			if(a.Frames != b.Frames) { return a.Frames > b.Frames; }
			return a.Tiles < b.Tiles;
		});
		if(kept.size() > kMaxPoses) {
			kept.resize(kMaxPoses);
		}
		LabelPoseFusions(kept, origins);
		//ADR-0179: variants by containment (§4), then the tracks (§1-2) and
		//what repetition finds on them (§3). All three read only the kept
		//table and the stream; none changes a pose or its rank.
		LabelPoseVariants(kept);
		std::vector<std::vector<TrackRun>> tracks = LinkPoseTracks(frames, vocab, kept, visible);
		stats.Poses = kept;
		FindPoseRuns(tracks, frames, stats);
		stats.Input = BuildInputStats(frames);
		stats.TrackRuns = tracks;
		return stats;
	}

	//ADR-0174 (issue #174): see SpriteGrouping.h. A cross-reference and nothing
	//more - it does not change what a sheet contains, how cells are grouped or
	//what a pose holds, so a consumer that ignores it reads the pack exactly as
	//before. That is the whole reason this, and not a change to the ADR-0153 §2
	//criterion, is the fix: the criterion decides the vocabulary and the layout
	//of every sheet, so loosening it re-cuts every pack ever recorded, while a
	//new optional field is additive in both directions.
	std::vector<uint32_t> PosesForCells(const PoseStats& stats, const std::vector<SheetCell>& cells)
	{
		std::set<uint32_t> nodes;
		for(const SheetCell& cell : cells) {
			if(cell.Metatile >= 0) {
				nodes.insert((uint32_t)cell.Metatile);
			}
		}
		std::vector<uint32_t> refs;
		if(nodes.empty()) {
			return refs;
		}

		//(covered, pose index) - covered descending, index ascending. The index
		//is already the ADR-0170 §1 rank (frames descending, then tiles), so
		//ties fall out in the order the file itself states.
		std::vector<std::pair<uint32_t, uint32_t>> scored;
		for(size_t i = 0; i < stats.Poses.size(); i++) {
			//ADR-0177: a fused entry is two figures that touched, not a figure.
			//This list exists so an artist can reach the subject a sheet's
			//cells belong to, and citing a fusion spends the kSheetMaxPoseRefs
			//budget on noise.
			if(!stats.Poses[i].FusionOf.empty()) {
				continue;
			}
			std::set<uint32_t> covered;
			for(const PoseTile& tile : stats.Poses[i].Tiles) {
				if(nodes.count(tile.Node)) {
					covered.insert(tile.Node);
				}
			}
			if(!covered.empty()) {
				scored.push_back(std::make_pair((uint32_t)covered.size(), (uint32_t)i));
			}
		}
		std::stable_sort(scored.begin(), scored.end(), [](const std::pair<uint32_t, uint32_t>& a, const std::pair<uint32_t, uint32_t>& b) {
			return a.first > b.first;
		});
		for(size_t i = 0; i < scored.size() && i < kSheetMaxPoseRefs; i++) {
			refs.push_back(scored[i].second);
		}
		return refs;
	}

	//See SpriteGrouping.h for why this is a spanning tree and not the pair table.
	std::vector<SpriteNearbyPlan> PlanSpriteNearby(const SheetGroup& group)
	{
		std::vector<SpriteNearbyPlan> plans;
		if(group.Cells.size() < 2 || group.Edges.empty()) {
			return plans;
		}

		//Only nodes the group actually placed can carry, or be named by, a
		//condition: a cell that is not on the sheet has no <tile> line to gate.
		std::map<uint32_t, uint32_t> countByNode;
		for(const SheetCell& cell : group.Cells) {
			if(cell.Metatile >= 0) {
				countByNode[(uint32_t)cell.Metatile] = cell.Count;
			}
		}
		if(countByNode.size() < 2) {
			return plans;
		}

		//Root: the most-seen cell, ties broken by the lower vocabulary index
		//(std::map iterates ascending), so one recording always roots one tree.
		uint32_t root = countByNode.begin()->first;
		uint32_t best = countByNode.begin()->second;
		for(const std::pair<const uint32_t, uint32_t>& entry : countByNode) {
			if(entry.second > best) {
				root = entry.first;
				best = entry.second;
			}
		}

		std::map<uint32_t, std::vector<const GroupEdge*>> byNode;
		for(const GroupEdge& edge : group.Edges) {
			if(edge.A == edge.B || !countByNode.count(edge.A) || !countByNode.count(edge.B)) {
				continue;
			}
			byNode[edge.A].push_back(&edge);
			byNode[edge.B].push_back(&edge);
		}

		std::set<uint32_t> seen;
		seen.insert(root);
		std::vector<uint32_t> queue;
		queue.push_back(root);
		for(size_t head = 0; head < queue.size(); head++) {
			uint32_t parent = queue[head];
			std::map<uint32_t, std::vector<const GroupEdge*>>::const_iterator it = byNode.find(parent);
			if(it == byNode.end()) {
				continue;
			}
			for(const GroupEdge* edge : it->second) {
				uint32_t child = edge->A == parent ? edge->B : edge->A;
				if(!seen.insert(child).second) {
					continue;
				}
				SpriteNearbyPlan plan;
				plan.Node = child;
				plan.Target = parent;
				//A GroupEdge reads "B sits at (Dx, Dy) from A". Seen from the
				//child, the parent sits at that same offset when the child is A,
				//and at its negation when the child is B.
				plan.Dx = child == edge->A ? edge->Dx : -edge->Dx;
				plan.Dy = child == edge->A ? edge->Dy : -edge->Dy;
				plan.Count = edge->Count;
				plan.ProbAB = edge->ProbAB;
				plan.ProbBA = edge->ProbBA;
				plans.push_back(plan);
				queue.push_back(child);
			}
		}
		return plans;
	}

	//See SpriteGrouping.h (issue #415) for why the palette comes from OAM.
	std::vector<uint32_t> SpriteNearbyPalettes(const std::vector<OamFrame>& frames, size_t shapeCount, const std::vector<uint32_t>& paletteColors)
	{
		//Per shape: palette id -> (frames seen, order of first sight).
		std::vector<std::map<PaletteId, std::pair<uint64_t, uint64_t>>> seen(shapeCount);
		uint64_t order = 0;
		for(const OamFrame& frame : frames) {
			for(const OamEntry& entry : frame.Entries) {
				if(entry.Shape >= shapeCount || entry.Palette == kUnknownPalette || entry.Palette >= paletteColors.size()) {
					continue;
				}
				if((paletteColors[entry.Palette] >> 24) != 0xFF) {
					continue; //not a sprite palette word: no evidence for a sprite condition
				}
				std::map<PaletteId, std::pair<uint64_t, uint64_t>>& counts = seen[entry.Shape];
				auto it = counts.find(entry.Palette);
				if(it == counts.end()) {
					it = counts.emplace(entry.Palette, std::make_pair((uint64_t)0, order++)).first;
				}
				it->second.first += frame.RepeatCount;
			}
		}

		std::vector<uint32_t> palettes(shapeCount, 0);
		for(size_t shape = 0; shape < shapeCount; shape++) {
			const std::pair<uint64_t, uint64_t>* best = nullptr;
			for(const auto& kv : seen[shape]) {
				if(!best || kv.second.first > best->first || (kv.second.first == best->first && kv.second.second < best->second)) {
					best = &kv.second;
					palettes[shape] = paletteColors[kv.first];
				}
			}
		}
		return palettes;
	}

	uint32_t NextStemIndex(const std::vector<std::string>& names, const std::string& prefix, const std::string& separator)
	{
		uint32_t next = 0;
		for(const std::string& name : names) {
			if(name.size() <= prefix.size() || name.compare(0, prefix.size(), prefix) != 0) {
				continue;
			}
			size_t end = prefix.size();
			while(end < name.size() && name[end] >= '0' && name[end] <= '9') {
				end++;
			}
			if(end == prefix.size()) {
				//Something else entirely under the same first letters.
				continue;
			}
			//The stem either ends the name or is followed by the separator our
			//own names use. A bare `spr003` is counted too: it is what a sheet
			//file is called, and numbering past it costs nothing, while reusing
			//it could collide with a condition an older builder wrote.
			if(end != name.size() && name.compare(end, separator.size(), separator) != 0) {
				continue;
			}
			//Clamped one below the top of uint32_t so that "highest + 1" below
			//cannot wrap to 0 and hand back a stem that is already in use. Only
			//a hand-edited name can reach the clamp; this builder's own stems
			//are the number of sprite groups in one recording.
			uint32_t index = (uint32_t)std::min<uint64_t>(
				std::strtoull(name.substr(prefix.size(), end - prefix.size()).c_str(), nullptr, 10), 0xFFFFFFFEull);
			next = std::max(next, index + 1);
		}
		return next;
	}
}
