import React from 'react';
import {Box, Text as InkText} from 'ink';
export {Box};
const colors: Record<string,string> = {clawd_body:'#d77757', clawd_background:'#000000', inverseText:'#000000'};
export const Text = ({color, backgroundColor, ...props}: any) => <InkText {...props} color={colors[color] ?? color} backgroundColor={colors[backgroundColor] ?? backgroundColor}/>;
