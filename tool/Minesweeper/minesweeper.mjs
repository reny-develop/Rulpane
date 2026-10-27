// Writes the bundled minesweeper rule set: its cell list, the graph of which cell touches which
// and the scrambled order are literals too long to keep by hand.
//
//   node tool/Minesweeper/minesweeper.mjs src/Rulpane.Viewer/wwwroot/rulesets/minesweeper.json
import { writeFileSync } from 'node:fs';

const W = 9, H = 9, MINES = 10, FACES = 64;
const files = 'abcdefghi'.slice(0, W).split('');
const names = [];
for (let r = H; r >= 1; r--) for (const f of files) names.push(f + r);
const xy = n => [files.indexOf(n[0]), +n.slice(1)];
const edges = {};
for (const n of names) {
  const [x, y] = xy(n);
  edges[n] = names.filter(m => { const [a, b] = xy(m); return m !== n && Math.abs(a - x) <= 1 && Math.abs(b - y) <= 1; });
}
// A fixed scramble of the cells, so that a mine layout picked by an affine step looks random.
let seed = 20261027;
const rand = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
const scrambled = [...names];
for (let i = scrambled.length - 1; i > 0; i--) { const j = Math.floor(rand() * (i + 1)); [scrambled[i], scrambled[j]] = [scrambled[j], scrambled[i]]; }
const units = [];
for (let a = 1; a < 81; a++) if (a % 3 !== 0) units.push(a);

const covered = { op: 'logic.or', any: [ { op: 'cmp.eq', left: '@v', right: 'h' }, { op: 'cmp.eq', left: '@v', right: 'm' } ] };
const isMine = v => ({ op: 'logic.or', any: [ { op: 'cmp.eq', left: v, right: 'm' }, { op: 'cmp.eq', left: v, right: 'fm' } ] });
const at = (b, c) => ({ op: 'grid.at', grid: b, coord: c });
// How many mines are around a square: written out where it is used rather than as a definition,
// whose memo would be keyed on the whole board.
const count = (b, c) => ({ op: 'seq.count', source: { op: 'graph.adjacent', graph: '$links', node: c }, as: 'k', where: isMine(at(b, '@k')) });
const cellsWhere = (as, predicate) => ({ op: 'seq.where', source: '#cells', as, predicate });

const rules = {
  $schema: 'rulealize/ruleset/v1',
  id: 'minesweeper',
  version: '1.0.0',
  requires: [
    { plugin: 'Rulealize.Plugin.TypeSchema', version: '^1.0' },
    { plugin: 'Rulealize.Plugin.State', version: '^1.0' },
    { plugin: 'Rulealize.Plugin.Sequence', version: '^1.2' },
    { plugin: 'Rulealize.Plugin.Comparison', version: '^1.0' },
    { plugin: 'Rulealize.Plugin.Logic', version: '^1.0' },
    { plugin: 'Rulealize.Plugin.Arithmetic', version: '^1.0' },
    { plugin: 'Rulealize.Plugin.Branch', version: '^1.0' },
    { plugin: 'Rulealize.Plugin.Binding', version: '^1.0' },
    { plugin: 'Rulealize.Plugin.Definition', version: '^1.0' },
    { plugin: 'Rulealize.Plugin.Grid', version: '^1.1' },
    { plugin: 'Rulealize.Plugin.Graph', version: '^1.0' },
    { plugin: 'Rulealize.Plugin.Chance', version: '^1.1' },
  ],
  state: {
    schema: {
      // h covered, m a covered mine, f and fm the same flagged, o open; once it is over, mine
      // every mine and x the one that went off. How many mines are around an open square is
      // worked out by the board projection, not kept: it is what the state already says.
      board: { op: 'grid.board', width: W, height: H, coord: 'algebraic',
        cell: { op: 'type.enum', values: ['h', 'm', 'f', 'fm', 'o', 'mine', 'x'], nullable: true } },
      tool: { op: 'type.enum', values: ['open', 'flag'] },
      placed: { op: 'type.bool' },
      // Which cell touches which: the shape a flood of open cells walks.
      links: { op: 'graph.of', nodes: names, edges, value: { op: 'type.bool' } },
    },
    initial: {
      board: Object.fromEntries(names.map(n => [n, 'h'])),
      tool: 'open',
      placed: false,
      links: Object.fromEntries(names.map(n => [n, false])),
    },
  },
  definitions: {
    cells: { op: 'seq.of', of: names },
    around: { params: ['c'], body: { op: 'graph.adjacent', graph: '$links', node: '@c' } },

    // Ten cells by a step through the scrambled order: a step coprime with 81 never comes back to
    // a cell, so the ten are distinct, and the first click and what touches it are skipped.
    place: {
      params: ['s', 'c'],
      body: {
        op: 'bind.let',
        bind: {
          step: { op: 'seq.elementAt', source: { op: 'seq.of', of: units }, index: { op: 'math.mod', left: '@s', right: units.length } },
          start: { op: 'math.mod', left: { op: 'math.mul', of: ['@s', 37] }, right: 81 },
          spared: { op: 'seq.concat', of: [ { op: 'seq.of', of: ['@c'] }, { op: 'def.call', def: 'around', args: { c: '@c' } } ] },
        },
        in: {
            op: 'seq.take', count: MINES,
            source: {
              op: 'seq.where', as: 'n',
              predicate: { op: 'logic.not', value: { op: 'seq.any', source: '@spared', as: 'x', predicate: { op: 'cmp.eq', left: '@x', right: '@n' } } },
              source: {
                op: 'seq.select', source: { op: 'seq.range', from: 0, count: MINES + 9 }, as: 'k',
                select: {
                  op: 'seq.elementAt', source: { op: 'seq.of', of: scrambled },
                  index: { op: 'math.mod', left: { op: 'math.add', of: ['@start', { op: 'math.mul', of: ['@k', '@step'] }] }, right: 81 },
                },
              },
            },
        },
      },
    },
    // Opening a cell with no mine around it opens everything around it, and on through every
    // such cell it reaches.
    open: {
      params: ['b', 'c'],
      body: {
        op: 'bind.let',
        bind: {
          calm: {
            op: 'graph.reachable', graph: '$links', from: '@c', as: 'n',
            through: { op: 'logic.and', all: [
              { op: 'cmp.eq', left: count('@b', '@n'), right: 0 },
              { op: 'logic.not', value: isMine(at('@b', '@n')) },
            ] },
          },
        },
        in: {
          op: 'bind.let',
          bind: {
            opened: {
              op: 'branch.if',
              cond: { op: 'cmp.eq', left: count('@b', '@c'), right: 0 },
              then: { op: 'seq.concat', of: ['@calm', { op: 'seq.selectMany', source: '@calm', as: 'z', select: { op: 'def.call', def: 'around', args: { c: '@z' } } }] },
              else: { op: 'seq.of', of: ['@c'] },
            },
          },
          in: { op: 'grid.withMany', grid: '@b', coords: '@opened', value: 'o' },
        },
      },
    },
    lost: { op: 'seq.any', source: { op: 'grid.cells', of: '$board' }, as: 'v', predicate: { op: 'cmp.eq', left: '@v', right: 'x' } },
    won: {
      op: 'logic.not',
      value: { op: 'seq.any', source: { op: 'grid.cells', of: '$board' }, as: 'v', predicate: { op: 'logic.or', any: [
        { op: 'cmp.eq', left: '@v', right: 'h' }, { op: 'cmp.eq', left: '@v', right: 'f' } ] } },
    },
  },
  inputs: {
    tap: {
      params: {
        at: { domain: cellsWhere('c', { op: 'bind.let', bind: { v: at('$board', '@c') }, in: {
          op: 'branch.if', cond: { op: 'cmp.eq', left: '$tool', right: 'flag' },
          then: { op: 'logic.or', any: [ covered, { op: 'cmp.eq', left: '@v', right: 'f' }, { op: 'cmp.eq', left: '@v', right: 'fm' } ] },
          else: covered } }) },
      },
      effects: [
        {
          op: 'state.set', path: 'board',
          value: {
            op: 'bind.let', bind: { v: at('$board', '@at') },
            in: {
              op: 'branch.if', cond: { op: 'cmp.eq', left: '$tool', right: 'flag' },
              then: { op: 'grid.with', grid: '$board', coord: '@at', value: { op: 'branch.match', value: '@v', cases: { h: 'f', m: 'fm', f: 'h', fm: 'm' } } },
              else: {
                op: 'branch.if', cond: { op: 'logic.not', value: '$placed' },
                then: { op: 'def.call', def: 'open', args: {
                  b: { op: 'grid.withMany', grid: '$board', value: 'm', coords: { op: 'def.call', def: 'place', args: { s: { op: 'math.add', of: [{ op: 'chance.roll', faces: FACES }, -1] }, c: '@at' } } },
                  c: '@at' } },
                else: {
                  op: 'branch.if', cond: { op: 'cmp.eq', left: '@v', right: 'm' },
                  then: { op: 'grid.with', coord: '@at', value: 'x',
                    grid: { op: 'grid.withMany', grid: '$board', value: 'mine', coords: cellsWhere('c', isMine(at('$board', '@c'))) } },
                  else: { op: 'def.call', def: 'open', args: { b: '$board', c: '@at' } },
                },
              },
            },
          },
        },
        { op: 'state.set', path: 'placed', value: { op: 'logic.or', any: ['$placed', { op: 'cmp.eq', left: '$tool', right: 'open' }] } },
      ],
    },
    flags: { when: { op: 'cmp.eq', left: '$tool', right: 'open' }, effects: [ { op: 'state.set', path: 'tool', value: 'flag' } ] },
    digs: { when: { op: 'cmp.eq', left: '$tool', right: 'flag' }, effects: [ { op: 'state.set', path: 'tool', value: 'open' } ] },
  },
  projections: {
    // The board as it is shown, square by square as a board is written: an open square as how
    // many mines are around it, blank for none, and every other as the state has it.
    board: Object.fromEntries(names.map(n => [n, {
      op: 'bind.let', bind: { v: at('$board', n) },
      in: {
        op: 'branch.if', cond: { op: 'cmp.eq', left: '@v', right: 'o' },
        then: { op: 'seq.elementAt', source: { op: 'seq.of', of: ['', '1', '2', '3', '4', '5', '6', '7', '8'] }, index: count('$board', n) },
        else: '@v',
      },
    }])),
    status: {
      tool: '$tool',
      left: { op: 'math.sub', left: MINES, right: { op: 'seq.count', source: { op: 'grid.cells', of: '$board' }, as: 'v', where: { op: 'logic.or', any: [
        { op: 'cmp.eq', left: '@v', right: 'f' }, { op: 'cmp.eq', left: '@v', right: 'fm' } ] } } },
    },
  },
  terminal: {
    when: { op: 'logic.or', any: ['#lost', { op: 'logic.and', all: ['$placed', '#won'] }] },
    result: { op: 'branch.if', cond: '#lost', then: 'lost', else: 'won' },
  },
};

const header = `{
  "$schema": "rulealize/ruleset/v1",
  "id": "minesweeper",
  "version": "1.0.0",

  // Minesweeper on a 9 × 9 board with ten mines. Written for Rulpane, by tool/Minesweeper: edit that
  // and run it, not this. The cell list, the graph of which cell touches which and the scrambled
  // order are generated, so they are not commented.
  //
  // The mines are laid by the first cell opened, never on it or next to it, from one roll of a
  // ${FACES}-sided die: that is the only draw, so the first click has ${FACES} outcomes and each is cheap.
  // A covered mine is "m" and a covered cell "h", and a view draws both the same.
`;
const body = JSON.stringify(rules, null, 2);
writeFileSync(process.argv[2], header + body.slice(body.indexOf('"requires"') - 2).replace(/^  "requires"/m, '\n  "requires"'));
