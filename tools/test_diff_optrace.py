# tools/test_diff_optrace.py (plain runner)
import sys
from diff_optrace import first_divergence, pick_scene_codebase, operand_filter
FAILS=[]
def check(c,m): (FAILS.append(m) or print("FAIL:",m)) if not c else print("ok:",m)

def test_equal_no_divergence():
    r = first_divergence([0,1,2,3],[0,1,2,3])
    check(r["index"] is None and r["agreed"]==4, "equal traces -> no divergence")

def test_first_divergence_point():
    r = first_divergence([0,1,2,9],[0,1,2,3])
    check(r["index"]==3 and r["a"]==9 and r["b"]==3, "divergence at first differing offset")

def test_prefix_shorter_vm():
    r = first_divergence([0,1,2,3],[0,1])          # vm ends early
    check(r["index"]==2 and r["b"] is None and r["agreed"]==2, "shorter VM trace flagged at end")

def test_pick_codebase_by_longest_common_prefix():
    entries=[{"codebase":100,"offset":0},{"codebase":100,"offset":5},   # cb100: [0,5,...]
             {"codebase":200,"offset":0},{"codebase":200,"offset":1},{"codebase":200,"offset":2}]
    check(pick_scene_codebase(entries,[0,1,2])==200, "codebase matching VM prefix chosen")

def test_operand_filter_drops_zero_operand_ops():
    # argc: 0x0->2, 0x5->0 (marker), 0xa->1, 0xf->0 => keep 0x0 and 0xa
    argc = {0x0:2, 0x5:0, 0xa:1, 0xf:0}
    check(operand_filter([0x0,0x5,0xa,0xf,0x5], argc)==[0x0,0xa], "operand_filter keeps only argc>=1 ops")

def main():
    test_equal_no_divergence(); test_first_divergence_point(); test_prefix_shorter_vm()
    test_pick_codebase_by_longest_common_prefix(); test_operand_filter_drops_zero_operand_ops()
    print("FAILURES:",len(FAILS)); return 1 if FAILS else 0
if __name__=="__main__": sys.exit(main())
