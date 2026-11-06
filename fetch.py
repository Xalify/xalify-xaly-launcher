# Downloads the itch.io store art for the games in games.json (for the launcher trailer). Temporary job.
import json,re,hashlib,os,time,urllib.request,html
os.makedirs("out/img",exist_ok=True)
UA={"User-Agent":"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126 Safari/537.36"}
def get(u):
    for i in range(3):
        try:
            with urllib.request.urlopen(urllib.request.Request(u,headers=UA),timeout=30) as r:return r.read()
        except Exception as e:
            print("retry",u,e);time.sleep(4*(i+1))
    return None
man={}
def img(u):
    if not u or u in man:return
    b=get(u)
    if not b:return
    n=hashlib.sha1(u.encode()).hexdigest()[:16]
    open("out/img/"+n,"wb").write(b);man[u]=n
for g in json.load(open("games.json")):
    p=get(g["url"])
    if not p:print("no page",g["url"]);continue
    t=p.decode("utf-8","replace");slug=re.sub(r"\W+","_",g["url"].split("//")[1])
    open("out/"+slug+".html","w").write(t);g["page"]="out/"+slug+".html"
    urls=[g.get("image")]
    m=re.search(r'<meta property="og:image" content="([^"]+)"',t);urls.append(m.group(1) if m else None)
    m=re.search(r'id="header"[^>]*>.*?<img[^>]+src="([^"]+)"',t,re.S);urls.append(m.group(1) if m else None)
    sl=re.search(r'class="screenshot_list"(.*?)</div>',t,re.S)
    if sl:urls+=re.findall(r'href="([^"]+)"',sl.group(1))[:4]
    fd=re.search(r'class="formatted_description[^"]*"(.*)',t,re.S)
    if fd:urls+=re.findall(r'<img[^>]+src="([^"]+)"',fd.group(1))[:5]
    for u in urls:img(html.unescape(u) if u else u)
    print(g["name"],len(man));time.sleep(1.5)
json.dump({"games":json.load(open("games.json")),"img":man},open("out/manifest.json","w"))
