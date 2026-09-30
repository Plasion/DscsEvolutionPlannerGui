import ast
from collections import deque
import csv
import heapq
import inspect
import re
import sys
from typing import Any, Callable, NamedTuple
from mohu import MohuMatcher

class Node(NamedTuple):
    point: int
    state: int

def bfs_search(graph: dict[int, list[int]], state_mappings: dict[int, int], start: Node, ends: list[int], end_state: int, forbidden_edges: set[tuple[Node, Node]], forbidden_nodes: set[Node]):
    if start in forbidden_nodes:
        return None
    if len(ends) > 0:
        for end_point in ends:
            if Node(end_point, end_state) not in forbidden_nodes:
                break
        else:
            return None

    queue = deque([start])
    parent : dict[Node, Node | None] = {start: None}
    visited = {start}
    end_node = None
    
    while queue:
        u = queue.popleft()
        if len(ends) > 0 and u.point in ends and u.state == end_state or len(ends) == 0 and u.state == end_state:
            end_node = u
            break
        for v_point in graph.get(u.point, []):
            if v_point not in graph: # 排除可能的有向边，方便世代过滤
                continue
            v = Node(v_point, u.state | state_mappings[v_point])
            
            if (u, v) in forbidden_edges or (v, u) in forbidden_edges: # 考虑无向边两种情况
                continue
            if v in forbidden_nodes:
                continue
            if v not in visited:
                visited.add(v)
                parent[v] = u
                queue.append(v)
    
    if end_node is None:
        return None
    # 重建路径
    path : list[Node] = []
    cur = end_node
    while cur is not None:
        path.append(cur)
        cur = parent[cur]
    path.reverse()
    return path

def yen_search(graph: dict[int, list[int]], state_mappings: dict[int, int], start: int, ends: list[int], end_state: int, k = 3) -> list[list[int]]:
    if start in ends and state_mappings[start] == end_state:
        return [[start]]
    
    # 候选路径堆
    candidates: list[list[Node]] = []
    # 最终结果列表
    A: list[list[Node]] = []
    
    # 第一条最短路径
    first_path = bfs_search(graph, state_mappings, Node(start, state_mappings[start]), ends, end_state, set(), set())
    if first_path is None:
        return []
    A.append(first_path)
    
    # 用于去重的路径集合（元组形式）
    path_set: set[tuple[Node, ...]] = set()
    path_set.add(tuple(first_path))
    
    # Yen 算法主循环：用已经找到的最后一条路径去产生候选
    for _ in range(1, k):
        # 取最新找到的路径用于偏离，即 A[-1]
        last_path = A[-1]
        
        # 尝试除终点外的其它点作为偏离点
        for i in range(len(last_path) - 1):
            spur_node = last_path[i] # 偏离点
            root_path = last_path[:i+1] # 前缀路径（包括偏离点）
            
            # 确定需要临时移除的边和禁止访问的节点
            # 1. 禁止访问前缀路径中除偏离点外的节点（保证简单路径）
            forbidden_nodes = set(root_path[:-1])
            # 2. 对 A 中所有前缀与前缀路径相同的路径，禁用它们从偏离点前往下一节点的那条边
            forbidden_edges : set[tuple[Node, Node]] = set()
            for path in A:
                if len(path) > i and path[:i+1] == root_path:
                    forbidden_edges.add((spur_node, path[i+1])) # 只加一种顺序，BFS 那会检查另一种顺序
            
            # 在当前受限图上，找偏离点到终点的最短路径
            spur_path = bfs_search(graph, state_mappings, spur_node, ends, end_state, forbidden_edges, forbidden_nodes)
            
            if spur_path is not None:
                # 拼接完整路径
                total_path = root_path + spur_path[1:]
                total_tuple = tuple(total_path)
                if total_tuple not in path_set:
                    # 加入候选堆
                    heapq.heappush(candidates, total_path)
                    path_set.add(total_tuple)
        
        # 若候选池为空，无法继续找到更多路径
        if not candidates:
            break
        
        # 弹出当前最短候选作为下一条路径
        next_path = heapq.heappop(candidates)
        A.append(next_path)

    # 后处理：把 Node 解包成 point
    return [[node.point for node in path] for path in A]

data_list: dict[int, dict[str, Any]]
name_mappings: dict[str, int]
link_graph: dict[int, list[int]]
empty_state_mappings: dict[int, int]
gen_mappings: dict[str, list[int]]
skill_mappings: dict[str, list[int]]
name_matcher = MohuMatcher(similarity_threshold=0.6)
gen_matcher = MohuMatcher(similarity_threshold=0.6)
skill_matcher = MohuMatcher(similarity_threshold=0.6)
skill_translate: Callable[[str], str]

def load_data():
    tmp_list: dict[str, dict[str, Any]] = {}
    with open('data.csv', mode='r', encoding='utf-8') as file:
        for row in csv.DictReader(file):
            tmp_item = {'编号': int(row['编号']), '名字': row['名字'], '别名': row['别名'] if row['别名'] != '' else None, '世代': row['世代'], '退化': ast.literal_eval(row['退化']), '进化': ast.literal_eval(row['进化']), '继承技': ast.literal_eval(row['继承技'])}
            tmp_list[tmp_item['名字']] = tmp_item
    for tmp_item in tmp_list.values():
        for i in range(len(tmp_item['退化'])):
            tmp_item['退化'][i] = tmp_list[tmp_item['退化'][i]]['编号']
        for i in range(len(tmp_item['进化'])):
            tmp_item['进化'][i] = tmp_list[tmp_item['进化'][i]]['编号']

        mapping = str.maketrans('ⅠⅡⅢ', '123')
        for i in range(len(tmp_item['继承技'])):
            tmp_item['继承技'][i] = tmp_item['继承技'][i].translate(mapping)

    global data_list, name_mappings, link_graph, empty_state_mappings, gen_mappings, skill_mappings, skill_translate

    data_list = {item['编号']: item for item in tmp_list.values()}
    empty_state_mappings = {item['编号']: 0 for item in tmp_list.values()}
    link_graph = {item['编号']: (item['退化'] + item['进化']) for item in tmp_list.values()}
    missings: set[int] = set()
    for _id, link_to in link_graph.items():
        for id_link_to in link_to:
            if _id not in link_graph[id_link_to]:
                link_graph[id_link_to].append(_id)
                missings.add(id_link_to)
    if len(missings) > 0:
        print(f'警告：{'、'.join([f'[{mid}]{data_list[mid]['名字']}' for mid in missings])} 进退化表不完整，已临时进行补全')

    name_mappings = {}
    for item in tmp_list.values():
        name_mappings[item['名字']] = item['编号']
        if item['别名'] is not None:
            name_mappings[item['别名']] = item['编号']
    name_matcher.build(list(name_mappings.keys()))
    gen_mappings = {}
    skill_mappings = {}
    for item in data_list.values():
        if item['世代'] not in gen_mappings:
            gen_mappings[item['世代']] = []
        gen_mappings[item['世代']].append(item['编号'])
        for skill in item['继承技']:
            skill_name = skill.split(sep='.')[1]
            if skill_name not in skill_mappings:
                skill_mappings[skill_name] = []
            skill_mappings[skill_name].append(item['编号'])
    gen_matcher.build(list(gen_mappings.keys()))
    skill_matcher.build(list(skill_mappings.keys()))

    mapping = {'Ⅰ': '1', 'Ⅱ': '2', 'Ⅲ': '3', 'I': '1', 'II': '2', 'III': '3', 'i': '1', 'ii': '2', 'iii': '3'}
    pattern = re.compile("|".join(map(re.escape, mapping)))
    skill_translate = lambda skill_name: pattern.sub(lambda m: mapping[m.group(0)], skill_name)

def find_digimon(id_or_name: str):
    try:
        _id = int(id_or_name)
        if _id in data_list:
            return _id
        else:
            return None
    except ValueError:
        name = name_matcher.match(id_or_name, max_results=1)
        if len(name) > 0:
            return name_mappings[name[0][0]]
        else:
            return None

def generate_output_text(evopath: list[int]):
    result = ''
    lastid = -1
    for _id in evopath:
        if lastid != -1:
            dataitem = data_list[_id]
            lastdataitem = data_list[lastid]
            if _id in lastdataitem['进化'] and lastid in dataitem['进化']: # 模式转换
                result += '⮂ '
            elif _id in lastdataitem['进化'] and lastid in dataitem['退化']: # 进化
                result += '↗ '
            elif _id in lastdataitem['退化'] and lastid in dataitem['进化']: # 退化
                result += '↘ '
            else: # 映射错误回落
                result += '→ '
        result += f'[{_id}]{data_list[_id]['名字']}'
        lastid = _id
    return result

def parse_command(params: list[str]):
    match params:
        case []:
            return
        case [('-?' | '/?' | '-h' | '-help' | '--help'), *_] | [('?' | 'h' | 'help')]:
            print(inspect.cleandoc('''
            用法：<起点数码兽id或名字> <<终点数码兽id或名字> | -em <终点数码兽id或名字> [更多数码兽...] | -a> [-r] [-k <需要的路径数量>] [-d <要排除的世代> [更多世代...]] [<-pm <途径数码兽id或名字>  [更多数码兽...]> [更多途经组...]] [-ps <途径数码兽需要具有的继承技> [更多继承技...]]
            退出：-e | --exit
            
            选项：
                -em 替代原终点数码兽，使之后给出的多只数码兽都可以作为终点。
                -a 替代原终点数码兽，使任意数码兽都可以作为终点。必须配合-pm或-ps使用。
                -r 可选参数，翻转路径显示，可以用来达成多起点进化到一终点的效果。
                -k 可选参数，需要求解前多少条最优路径。默认为3。
                -d 可选参数，禁止之后给出的多个世代的数码兽参与路径计算。
                -pm 可选参数，要求路径必须途径之后给出的多只数码兽之一。可多次使用以添加多个途经组。
                -ps 可选参数，要求路径必须能够收集之后给出的所有继承技。可以用2、II或ii来表示Ⅱ，其它同理。
            '''))
            return
        case [('-e' | '-exit' | '--exit'), *_] | [('e' | 'exit')]:
            sys.exit()
            return
        case [_]:
            print('错误：参数太少了。输入?、-h或--help查看用法。')
            return
        case [start_id_or_name, *others] if (start := find_digimon(start_id_or_name)) is not None:
            ends: list[int] = []
            match others:
                case [('-em')]:
                    print('错误：-em需要至少一个参数。输入?、-h或--help查看用法。')
                    return
                case ['-em', *others]:
                    for i in range(len(others)):
                        if others[i] in {'-r', '-k', '-d', '-pm', '-ps'}:
                            end_id_or_names, others = others[:i], others[i:]
                            break
                    else:
                        end_id_or_names, others = others, []
                    for end_id_or_name in end_id_or_names:
                        if (tmp_end := find_digimon(end_id_or_name)) is None:
                            print(f'错误：找不到终点数码兽「{end_id_or_name}」。')
                            return
                        ends.append(tmp_end)
                    # 下溢以处理可选选项
                case ['-a', *others]:
                    for i in range(len(others)):
                        if others[i] in {'-pm', '-ps'}:
                            break
                    else:
                        print('错误：使用了-a但未使用-pm或-ps进行约束。输入?、-h或--help查看用法。')
                        return
                    # 下溢以处理可选选项
                case [end_id_or_name, *others] if (end := find_digimon(end_id_or_name)) is not None:
                    ends.append(end)
                    # 下溢以处理可选选项
                case _:
                    print(f'错误：找不到终点数码兽「{end_id_or_name}」。')
                    return
        case _:
            print(f'错误：找不到起点数码兽「{start_id_or_name}」。')
            return

    # 处理可选选项
    flags: list[list[str]] = []
    i = 0
    for j in range(len(others)):
        if others[j] in {'-r', '-k', '-d', '-pm', '-ps'}:
            flags.append(others[i:j])
            i = j
    if len(others) > 0:
        flags.append(others[i:])

    reverse = False
    k = 3
    used_graph = link_graph
    used_state_mappings = empty_state_mappings
    state_count = 0
    for flag in flags:
        match flag:
            case ['-k']:
                print('错误：-k需要一个参数。输入?、-h或--help查看用法。')
                return
            case [('-d' | '-pm' | '-ps') as flag_type]:
                print(f'错误：{flag_type}需要至少一个参数。输入?、-h或--help查看用法。')
                return
            case ['-r', *_]:
                reverse = True
                # 下溢以继续处理其它选项
            case ['-k', k_str, *others]:
                try:
                    k = int(k_str)
                except ValueError:
                    print(f'错误：提供给-k的参数「{k_str}」不是数字。')
                    return
                if k < 1:
                    print(f'错误：提供给-k的参数「{k}」太小了，应该至少为1。')
                    return
                # 下溢以继续处理其它选项
            case ['-d', *others]:
                used_graph = link_graph.copy()
                for gen_str in others:
                    gen = gen_matcher.match(gen_str, max_results=1)
                    if len(gen) > 0:
                        for _id in gen_mappings[gen[0][0]]:
                            del used_graph[_id] # 只删节点省事，剩下的有向边让 BFS 那处理
                    else:
                        print(f'错误：找不到世代「{gen_str}」。')
                        return
                # 下溢以继续处理其它选项
            case ['-pm', *others]:
                if used_state_mappings is empty_state_mappings:
                    used_state_mappings = empty_state_mappings.copy()
                for pass_id_or_name in others:
                    tmp_pass = find_digimon(pass_id_or_name)
                    if tmp_pass is None:
                        print(f'错误：找不到途径数码兽「{pass_id_or_name}」。')
                        return
                    used_state_mappings[tmp_pass] |= (1 << state_count)
                state_count += 1
                # 下溢以继续处理其它选项
            case ['-ps', *others]:
                if used_state_mappings is empty_state_mappings:
                    used_state_mappings = empty_state_mappings.copy()
                for skill_str in others:
                    skill = skill_matcher.match(skill_translate(skill_str), max_results=1)
                    if len(skill) > 0:
                        for _id in skill_mappings[skill[0][0]]:
                            used_state_mappings[_id] |= (1 << state_count)
                        state_count += 1
                    else:
                        print(f'错误：找不到继承技「{skill_str}」。')
                        return
                # 下溢以继续处理其它选项

    end_state = (1 << state_count) - 1
    results = yen_search(used_graph, used_state_mappings, start, ends, end_state, k)
    if len(results) == 0:
        print('错误：找不到可用进化路线')
        return
    for i, result in enumerate(results, 1):
        if reverse:
            result.reverse()
        print(f'进化路线{i}：' + generate_output_text(result))

def main():
    load_data()
    if len(sys.argv) > 1:
        params = sys.argv[1:]
        run_once = True
    else:
        run_once = False
    while True:
        if not run_once:
            params = input('>>>').split()
        parse_command(params)
        if run_once:
            break

if __name__ == '__main__':
    main()