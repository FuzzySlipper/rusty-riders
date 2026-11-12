"""Submits one MiniMax H3 text-to-video job to a ComfyUI host (asset-pipeline's minimax_h3_t2v_api.json graph); prints its prompt id.

    h3.py COMFY_URL SEED PROMPT OUTPUT_PREFIX
"""
import json,sys,urllib.request,uuid
url,seed,prompt,prefix=sys.argv[1],int(sys.argv[2]),sys.argv[3],sys.argv[4]
import os
g=json.load(open(os.environ.get('H3_GRAPH', os.path.expanduser('~/dev/asset-pipeline/legacy/tools/asset-cli/operators/video/minimax_h3_t2v_api.json'))))
g['5']['inputs']['prompt']=prompt; g['6']['inputs']['noise_seed']=seed; g['14']['inputs']['filename_prefix']=prefix
req=urllib.request.Request(url+'/prompt',json.dumps({'prompt':g,'client_id':str(uuid.uuid4())}).encode(),{'Content-Type':'application/json'})
print(json.load(urllib.request.urlopen(req))['prompt_id'])
