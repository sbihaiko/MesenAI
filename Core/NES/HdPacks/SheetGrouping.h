#pragma once
//ADR-0153 §2 (Phase 9): mutual-predictability grouping - the criterion that
//replaces F5.4e's "seen adjacent >= 2 times" union-find, which collapsed any
//contiguous scene into one component and so never emitted an object sheet on a
//real game. Host-free (see TileSheetTypes.h): no pch.h, no Emulator, no I/O.
//HdPackBuilder feeds the vocabulary in and writes the bytes SheetRender makes
//out of the groups returned here.
#include "NES/HdPacks/TileSheetTypes.h"

namespace MesenSheets
{
	//Path-halving union-find over cell indexes. Header-level because both
	//grouping modules need the same components: SheetGrouping over predictive
	//edges, SpriteGrouping over an OAM frame's spatial clusters (ADR-0170).
	class Dsu
	{
	public:
		explicit Dsu(size_t size) : _parent(size)
		{
			for(size_t i = 0; i < size; i++) {
				_parent[i] = (uint32_t)i;
			}
		}

		uint32_t Find(uint32_t node)
		{
			while(_parent[node] != node) {
				_parent[node] = _parent[_parent[node]];
				node = _parent[node];
			}
			return node;
		}

		void Union(uint32_t a, uint32_t b)
		{
			uint32_t rootA = Find(a);
			uint32_t rootB = Find(b);
			if(rootA != rootB) {
				_parent[rootA] = rootB;
			}
		}

	private:
		std::vector<uint32_t> _parent;
	};

	//ADR-0153 §2: edges that pass the mutual-predictability test, in both
	//directions, with their evidence. Deterministic order.
	std::vector<GroupEdge> SelectPredictiveEdges(const Vocabulary& vocab, uint32_t minCount, double minProb);

	//DSU over those edges; components of 2..kSheetMaxObjectCells cells become
	//objects, laid out by BFS at their dominant E/S offsets.
	std::vector<SheetGroup> BuildObjects(const Vocabulary& vocab, uint32_t minCount, double minProb);

	//Convenience overload using kSheetMinPairCount / kSheetMinPairProb.
	std::vector<SheetGroup> BuildObjects(const Vocabulary& vocab);

	//The half of the criterion that does not care where the edges came from,
	//shared with SpriteGrouping (F9.5): components of 2..kSheetMaxObjectCells
	//cells, then a BFS layout at each edge's Dx/Dy, biggest group first.
	std::vector<SheetGroup> LayoutGroups(const Vocabulary& vocab, const std::vector<GroupEdge>& edges);

	//ADR-0175 (issue #175): the slots of a group's Columns x Rows grid that no
	//member occupies, row-major. A group sheet's grid is the *bounding box* of
	//a BFS layout, so a figure that is not a rectangle - an L-shaped ledge, the
	//two rows of a "GAME OVER" - leaves blanks in it by construction. They are
	//not missing art and there is nothing to paint in them; without this list
	//an artist cannot tell a deliberate blank from a subject the recorder
	//failed to place, which is exactly what issue #175 reported.
	std::vector<SheetSlot> EmptyGroupSlots(const SheetGroup& group);

	//ADR-0190: one observed background adjacency, as HdPackBuilder's
	//co-occurrence table holds it. Ordered and direction-carrying - "B sat one
	//cell east (or south) of A" - because the tileNearby it may become has to
	//state an offset with a sign.
	struct TileAdjacency
	{
		uint32_t A = 0;
		uint32_t B = 0;
		bool South = false;
		uint32_t Frames = 0;     //accumulated frames the adjacency held in
		uint32_t FramesA = 0;    //frames A was on screen in at all
		uint32_t FramesB = 0;    //frames B was on screen in at all
		bool InObject = false;   //both shapes belong to an inferred object
	};

	//The half of the tileNearby decision that does not touch emulator state, so
	//it can be tested without one (ADR-0127). Keeps an adjacency when both its
	//shapes are inside an inferred object, it held over at least minFrames
	//accumulated frames, and it accounts for at least minProb of the frames each
	//of its two shapes appeared in - read in BOTH directions, so a merely common
	//shape never becomes everyone's neighbour. Returns indexes into `edges`, in
	//input order.
	std::vector<size_t> SelectTileNearby(const std::vector<TileAdjacency>& edges, uint32_t minFrames, double minProb);

	//Issue #232: the index a new `<prefix>N` name must start at, given the names
	//already defined - highest N seen, plus one; 0 when none match. Only a suffix
	//of digits counts, so a name that merely starts with the prefix is not
	//mistaken for a numbered one. Pure, so it is unit-tested here rather than
	//through the builder (ADR-0127): HdPackBuilder merges with the pack it loaded,
	//whose tiles hold raw pointers to its HdPackCondition objects, so a
	//`tileNearby` name from an earlier session can neither be reused (it would
	//re-point live art at different evidence) nor deleted (use-after-free) - the
	//counter has to number past it, or the edge is dropped in silence.
	uint32_t NextNameIndex(const std::vector<std::string>& names, const std::string& prefix);
}
