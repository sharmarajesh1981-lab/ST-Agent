const form=document.querySelector('#task-form'),table=document.querySelector('#tasks');
async function refresh(){const response=await fetch('/api/tasks');const tasks=await response.json();table.replaceChildren(...tasks.map(task=>{const row=document.createElement('tr');row.innerHTML=`<td>${escape(task.status)}</td><td>${escape(task.prompt)}</td><td>${escape(task.result||task.error||'')}</td>`;return row;}));}
function escape(value){const span=document.createElement('span');span.textContent=value??'';return span.innerHTML;}
form.addEventListener('submit',async event=>{event.preventDefault();const prompt=document.querySelector('#prompt').value.trim();const model=document.querySelector('#model').value.trim();if(!prompt)return;await fetch('/api/tasks',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({prompt,model})});form.reset();document.querySelector('#model').value=model||'llama3.2';await refresh();});
refresh();setInterval(refresh,2000);
