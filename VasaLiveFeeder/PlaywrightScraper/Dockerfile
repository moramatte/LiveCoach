# Use official Playwright image with Chromium pre-installed
FROM mcr.microsoft.com/playwright:v1.48.2-jammy

# Set working directory
WORKDIR /app

# Install Node.js dependencies
COPY package.json ./
RUN npm install --production

# Copy application code
COPY server.js ./

# Expose port
EXPOSE 3000

# Run the service
CMD ["node", "server.js"]
