// npm package: KhronosGroup glTF-Validator (install in .tools/gltf-validator/package).
const fs=require('fs');
const validator=require('../.tools/gltf-validator/package');
(async()=>{
  for(const file of process.argv.slice(2)){
    const report=await validator.validateBytes(new Uint8Array(fs.readFileSync(file)),{uri:file,maxIssues:2000});
    const {messages,...counts}=report.issues;
    console.log(JSON.stringify({file,validator:validator.version(),...counts,codes:[...new Set(messages.map(x=>x.code))],errors:messages.filter(x=>x.severity<2)},null,2));
    if(report.issues.numErrors)process.exitCode=1;
  }
})().catch(error=>{console.error(error);process.exitCode=1;});
