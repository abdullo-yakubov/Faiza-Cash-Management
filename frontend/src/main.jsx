import React from 'react';
import {createRoot} from 'react-dom/client';
import App from './app/App';
import './styles/app.css';
import './styles/refinements.css';
import './styles/animations.css';
import './styles/password-input.css';
import './styles/access-management.css';
import './styles/tokens.css';
import './styles/ui.css';
import {setDemo} from './services/demo.js';
// ?demo=1 включает, ?demo=0 выключает демо-данные волны 1 (запоминается в localStorage).
{const flag=new URLSearchParams(window.location.search).get('demo');if(flag==='1')setDemo(true);if(flag==='0')setDemo(false);}
createRoot(document.getElementById('root')).render(<App/>);
