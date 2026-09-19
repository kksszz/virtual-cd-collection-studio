// Independent generic viewer test: strips every extras object before loading with Three.js.
// All resources are served locally; no album data is uploaded.
const fs=require('fs'),http=require('http'),path=require('path');
const {chromium}=require('../.tools/playwright-core/package');
const source=fs.readFileSync(process.argv[2]);
const jsonLength=source.readUInt32LE(12),root=JSON.parse(source.subarray(20,20+jsonLength).toString());
function strip(value){if(value&&typeof value==='object'){delete value.extras;for(const x of Object.values(value))strip(x);}}strip(root);
const json=Buffer.from(JSON.stringify(root)),padded=Buffer.alloc((json.length+3)&~3,32);json.copy(padded);
const bin=source.subarray(20+jsonLength),head=Buffer.alloc(20);head.writeUInt32LE(0x46546c67,0);head.writeUInt32LE(2,4);head.writeUInt32LE(20+padded.length+bin.length,8);head.writeUInt32LE(padded.length,12);head.writeUInt32LE(0x4e4f534a,16);
const portable=Buffer.concat([head,padded,bin]);
const threeRoot=path.resolve(__dirname,'../.tools/three/package');
const html=`<!doctype html><meta charset="utf-8"><style>body{margin:0;background:#111920}canvas{display:block}</style>
<script type="importmap">{"imports":{"three":"/three/build/three.module.js","three/addons/":"/three/examples/jsm/"}}</script>
<script type="module">
import * as THREE from 'three';import {GLTFLoader} from 'three/addons/loaders/GLTFLoader.js';
try{
const gltf=await new GLTFLoader().loadAsync('/asset.glb');
const renderer=new THREE.WebGLRenderer({antialias:true,preserveDrawingBuffer:true});renderer.setSize(1000,700);document.body.appendChild(renderer.domElement);
const scene=new THREE.Scene();scene.background=new THREE.Color(0x111920);scene.add(gltf.scene);scene.add(new THREE.HemisphereLight(0xffffff,0x607080,3));
const light=new THREE.DirectionalLight(0xffffff,3);light.position.set(-1,2,3);scene.add(light);
const camera=new THREE.PerspectiveCamera(42,1000/700,.001,10);const mixer=new THREE.AnimationMixer(gltf.scene);
function draw(){gltf.scene.updateMatrixWorld(true);const box=new THREE.Box3().setFromObject(gltf.scene),center=box.getCenter(new THREE.Vector3()),size=box.getSize(new THREE.Vector3());const distance=Math.max(size.x/1.42,size.y)*1.8;camera.position.copy(center).add(new THREE.Vector3(.035,.035,distance));camera.lookAt(center);renderer.render(scene,camera);return size.toArray();}
window.drawPose=name=>{mixer.stopAllAction();if(name){const clip=THREE.AnimationClip.findByName(gltf.animations,name);const action=mixer.clipAction(clip);action.setLoop(THREE.LoopOnce,1);action.clampWhenFinished=true;action.play();mixer.update(clip.duration);}return draw();};
const size=draw();let meshes=0;gltf.scene.traverse(o=>{if(o.isMesh)meshes++;});
window.testResult={meshes,animations:gltf.animations.map(a=>a.name),size,extrasStripped:true};
}catch(e){window.testError=String(e.stack||e);}
</script>`;
const server=http.createServer((req,res)=>{
  if(req.url==='/'){res.setHeader('Content-Type','text/html');res.end(html);return;}
  if(req.url==='/asset.glb'){res.setHeader('Content-Type','model/gltf-binary');res.end(portable);return;}
  if(req.url.startsWith('/three/')){const target=path.resolve(threeRoot,req.url.slice(7));if(target.startsWith(threeRoot+path.sep)&&fs.existsSync(target)){res.setHeader('Content-Type','text/javascript');res.end(fs.readFileSync(target));return;}}
  res.statusCode=404;res.end();
});
(async()=>{await new Promise(r=>server.listen(0,'127.0.0.1',r));let browser;
try{browser=await chromium.launch({executablePath:'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',headless:true});const page=await browser.newPage({viewport:{width:1000,height:700}});
await page.goto('http://127.0.0.1:'+server.address().port);await page.waitForFunction(()=>window.testResult||window.testError,{},{timeout:30000});
const result=await page.evaluate(()=>window.testResult||{error:window.testError});if(result.error)throw Error(result.error);
if(result.meshes<1||!result.animations.includes('DiscOut')||result.size[0]<.1||result.size[0]>.2)throw Error('Missing standard geometry, animation or metre-scale');
await page.screenshot({path:path.resolve(__dirname,'../.tools/glb-generic-closed.png')});await page.evaluate(()=>window.drawPose('DiscOut'));
await page.screenshot({path:path.resolve(__dirname,'../.tools/glb-generic-open.png')});console.log(JSON.stringify({PASS:result}));
}finally{if(browser)await browser.close();server.close();}})().catch(e=>{console.error(e);process.exitCode=1;server.close();});
