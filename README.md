# WhatHuh
 
Automaticly Create Subtitles For Video Files

So if you have older movies or tv shows that it is impossible to get subtitles for or you want to easily sub movies you create this is about as easy as it can be.
WhatHuh comes from the frustration of there not being an easy way to get or create subs so that I can still enjoy watching things despite my hearing loss.  
This project uses OpenAI's Whisper model to create the subtitles.  There are several model options, Base, Base with special English training, small, medium and 3 versions of large.  The bigger the model, the more demanding it is on your system, both in RAM, VRAM and or CPU.  

Many of you will go right for LargeV3 thinking, I need the best and bigger is better.  Try the base model first.  It is astonishingly accurate and fast.
If you inist on using the more advanced models you have only to select them in the dropdown and WhatHuh will download them for you and keep them on your HD so you don't need to download them again.

![image](https://github.com/Echostorm44/WhatHuh/assets/107306362/8190155a-1935-4b35-a701-42638826bf7b)


There are 2 releases, 1 for CPU which as you may have guessed only uses your CPU to create the subtitles and is pretty fast as is.  It is a portable install and only requires you to download a big ZIP file and run an exe.
The other release is CUDA enabled and can make the process much faster, though it is already quite fast on CPU only.  If you want to go the CUDA route and use the power of your graphics card you'll need to install the CUDA tookkit and reboot your pc.  I know it sucks but needs must.  We're looking at ways to get around that requirement.   https://developer.nvidia.com/cuda-downloads

![image](https://github.com/Echostorm44/WhatHuh/assets/107306362/e5cc0ae8-6d03-4448-8c86-a8a4b71c1e13)


![image](https://github.com/Echostorm44/WhatHuh/assets/107306362/9073951e-3c90-40f8-978d-63d89df7e7d1)

When it is done it drops the srt subtitle file next to the video file source.

![image](https://github.com/Echostorm44/WhatHuh/assets/107306362/c4ed959e-7803-45b5-85e4-cd147b8991ea)
