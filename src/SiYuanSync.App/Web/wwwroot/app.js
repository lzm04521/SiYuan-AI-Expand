// 占位，Task 22 完善
fetch('/api/config').then(r=>r.text()).then(t=>document.getElementById('app').textContent=t);
